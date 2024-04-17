import os
import pyesapi as pe
from array import *
import numpy as np
import datetime as dt
from inspect import getsourcefile
import h5py
from DataClasses import *
import json
from json import JSONEncoder

spotWeight = 100

class Helpers : 
    @staticmethod
    def PopulateBeamData(hPlan:pe.PlanSetup, hBeam:pe.Beam ) : 
        hBeamData = BeamMetaData()
        hBeamData.ID = hBeam.Id
        hBeamData.iso_center = IsocenterPosition(hBeam.IsocenterPosition.x, hBeam.IsocenterPosition.y, hBeam.IsocenterPosition.z)
        hBeamData.jaw_position = JawPosition(hBeam.ControlPoints[0].JawPositions)
        hBeamData.doseMatrixSizeX = hBeam.Dose.XSize
        hBeamData.doseMatrixSizeY = hBeam.Dose.YSize
        hBeamData.doseMatrixSizeZ = hBeam.Dose.ZSize
        return hBeamData
    
    @staticmethod
    def Log(szMsg):
        print(str(dt.datetime.now()) + ': ' + szMsg)

    @staticmethod
    def GetIonPlan(app, patientId, courseId, planId):
        Helpers.Log('Opening Patient \"' + patientId + '\"')
        patient = app.OpenPatientById(patientId)
        if (patient is None) :
            raise Exception('Could not find Patient with ID \"' + {patientId} + '\"')
                                        
        patient.BeginModifications()

        Helpers.Log('Opening Plan \"' + courseId + '/' +  planId + '\"')
        course = next(c for c in patient.Courses if c.Id.lower() == courseId.lower()) #patient.Courses.SingleOrDefault(x => x.Id.ToLower() == courseId.ToLower())
        if (course is None) :
            raise Exception('Could not find Course with ID \"' + {courseId} + '\"')
        
        plan = next(p for p in course.IonPlanSetups if p.Id.lower() == planId.lower()) #course.IonPlanSetups.SingleOrDefault(x => x.Id.ToLower() == planId.ToLower());
        if (plan is None) :
            raise Exception('Could not find IonPlan with ID \"' + {planId} + '\"')
        
        Helpers.Log(planId + ' found.')
        return plan
        
    @staticmethod
    def SetAllSpotsToZero(hPlan):
        for field in hPlan.IonBeams :
            fieldParams = field.GetEditableParameters() 
            icpps = fieldParams.IonControlPointPairs
            for layerIdx in range(icpps.Count) :
                rawSpotList = icpps[layerIdx].RawSpotList
                for spotIdx in range(rawSpotList.Count) :
                    rawSpotList[spotIdx].Weight = 0.0
            field.ApplyParameters(fieldParams)

    @staticmethod
    def GetNonZeroDosePoints(hDose:pe.BeamDose, arrFullDoseMatrix:np.array) :
        if (hDose is None) :
            raise Exception("Dose does not exist.")

        #dIntercept = hDose.VoxelToDoseValue(0).Dose
        #dScale = hDose.VoxelToDoseValue(1).Dose - dIntercept
        iXSize = hDose.XSize
        iYSize = hDose.YSize
        iZSize = hDose.ZSize

        arr3DVoxels = hDose.np_array_like() # 3D dose matrix AFTER scale and intercept are applied
        doseFromSpot = list()
        for sliceIndex in range(iZSize) : 
            doseBuffer = arr3DVoxels[:,:,sliceIndex]
            #voxels = arrVoxels
            for y in range(iYSize) :
                for x in range(iXSize) :
                    doseVal = doseBuffer[x,y]
                    if (doseVal > 0) :
                        pointDose = doseVal / spotWeight
                        doseFromSpot.append(DosePoint(x, y, sliceIndex, pointDose))
                    arrFullDoseMatrix[0, sliceIndex, y, x] = doseVal
        return DoseData(doseFromSpot)

    @staticmethod
    def WriteResults_CVS(doseData: DoseData, filepath) :
        builder = []
        header = 'sliceIndex,yIndex,xIndex,dose'
        builder.append(header)
        for dosePoint in doseData.dosePoints :
            builder.append(str(dosePoint.sliceIndex)+ ',' + str(dosePoint.indexY) + ',' + str(dosePoint.indexX) +',' + '{0:.7f}'.format(dosePoint.doseValue))

        with open(filepath, 'w') as f:
            f.write('\n'.join(builder))

    @staticmethod
    def WriteResults_HDF5(hBeamData:BeamMetaData, arrFullDoseMatrix:np.array, doseData:DoseData, iLayerIdx:int, iSpotIdx:int, szPath:str) : 
        szDataFile = szPath + "\Beam_" + hBeamData.ID + "_Data.h5"
        Helpers.WriteInfMatrixHDF5(hBeamData, arrFullDoseMatrix, doseData, iLayerIdx, iSpotIdx, szDataFile)

    @staticmethod
    def WriteBeamMetaData(hBeamData:BeamMetaData, szPath) :
        szMetaDataFile = szPath + "\Beam_" + hBeamData.ID + "_MetaData.json"
        if( not os.path.exists(szMetaDataFile) ) :
            Helpers.WriteJasonFile(hBeamData, szMetaDataFile)

    @staticmethod
    def WriteJasonFile(hObj, szPath):
        json_object = json.dumps(hObj, indent=4, cls=ObjectEncoder)
        # Writing to sample.json
        with open(szPath, "w") as outfile:
            outfile.write(json_object)

    @staticmethod
    def AddOrAppendDataSet(hf:h5py.File, szDataSetName:str, arrData:np.array) :
        if(not (szDataSetName in hf) ) :
            lst = list(arrData.shape)
            lst[0] = None
            s=tuple((lst))
            hf.create_dataset(szDataSetName, compression="gzip", chunks=True, data=arrData, maxshape=s)
        else:
            hf[szDataSetName].resize((hf[szDataSetName].shape[0] + arrData.shape[0]), axis = 0)
            hf[szDataSetName][-arrData.shape[0]:] = arrData

    @staticmethod
    def WriteInfMatrixHDF5(hBeamData:BeamMetaData, arrFullDoseMatrix:np.array, doseData:DoseData, iLayerIdx, iSpotIdx, szPath) :
        bNewFile:bool = os.path.exists(szPath)

        with h5py.File(szPath, 'a') as hf:

            # write full inf matrix
            Helpers.AddOrAppendDataSet(hf, '/inf_matrix_full', arrFullDoseMatrix)

            # write sparse inf matrix
            lstDosePoints = doseData.dosePoints
           
            iPtCnt = len(lstDosePoints)
            arrSparse = np.zeros((iPtCnt, 4))
            iSliceSize = hBeamData.doseMatrixSizeX * hBeamData.doseMatrixSizeY
            for idx in range(iPtCnt) : 
                dp:DosePoint = lstDosePoints[idx]
                arrSparse[idx, 0] = iLayerIdx
                arrSparse[idx, 1] = iSpotIdx
                arrSparse[idx, 2] = dp.sliceIndex * iSliceSize + dp.indexY * hBeamData.doseMatrixSizeX + dp.indexX
                arrSparse[idx, 3] = dp.doseValue
            Helpers.AddOrAppendDataSet(hf, '/inf_matrix_sparse', arrSparse)

class ObjectEncoder(JSONEncoder):
        def default(self, o):
            return o.__dict__
