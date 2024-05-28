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
    def PopulateBeamData(hBeam:pe.Beam) : 
        firstCP:pe.ControlPoint = hBeam.ControlPoints[0]
        hBeamData = TBeamMetaData()
        hBeamData.Id = hBeam.Id
        hBeamData.szEnergy = hBeam.EnergyModeDisplayName
        hBeamData.szTechnique = hBeam.Technique.Id
        hBeamData.szMLCName = "" if hBeam.MLC==None else hBeam.MLC.Name
        hBeamData.fGantryRtn = firstCP.GantryAngle
        hBeamData.fCollRtn = firstCP.CollimatorAngle
        hBeamData.fPatientSuppAngle = firstCP.PatientSupportAngle
        hBeamData.fIsoX = hBeam.IsocenterPosition.x
        hBeamData.fIsoY = hBeam.IsocenterPosition.y
        hBeamData.fIsoZ = hBeam.IsocenterPosition.z
        hBeamData.fJawX1 = firstCP.JawPositions.X1
        hBeamData.fJawX2 = firstCP.JawPositions.X2
        hBeamData.fJawY1 = firstCP.JawPositions.Y1
        hBeamData.fJawY2 = firstCP.JawPositions.Y2
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
    def WriteResults_HDF5(hBeamData:TBeamMetaData, szMachine, fInfCutoffValue, arrFullDoseMatrix:np.array, doseData:DoseData, iLayerIdx:int, iSpotIdx:int, szPath:str) : 
        szPath = szPath + "\\Beams"
        if not os.path.exists(szPath):
            os.mkdir(szPath)
            
        szBeamMetaDataFile = szPath + "\Beam_" + hBeamData.Id + "_MetaData.json"
        Helpers.WriteBeamMetaData(hBeamData, szMachine, fInfCutoffValue, szBeamMetaDataFile)
        
        szHDF5DataFile = szPath + "\Beam_" + hBeamData.Id + "_Data.h5"
        Helpers.WriteInfMatrixHDF5(arrFullDoseMatrix, doseData, iLayerIdx, iSpotIdx, szHDF5DataFile)
          
    @staticmethod
    def WriteBeamMetaData(echoBeam:TBeamMetaData, szMachine:str, fInfMatrixCutoffValue:float, szOuputFile:str) :
        szBeamID = echoBeam.Id
        szFilename:str = "Beam_" + szBeamID + "_Data.h5"
        dctBeamData =  { 
            "ID" : szBeamID,
            "gantry_angle" : echoBeam.fGantryRtn,
            "collimator_angle" : echoBeam.fCollRtn,
            "couch_angle" : echoBeam.fPatientSuppAngle,
            'iso_center' : {'x_mm':echoBeam.fIsoX, 'y_mm':echoBeam.fIsoY, 'z_mm':echoBeam.fIsoZ},
            'beamlets' : { 'id_File' : szFilename + "/beamlets/id",
                            'width_mm_File' : szFilename + "/beamlets/width_mm",
                            'height_mm_File' : szFilename + "/beamlets/height_mm",
                            'position_x_mm_File' : szFilename + "/beamlets/position_x_mm",
                            'position_y_mm_File' : szFilename + "/beamlets/position_y_mm",
                            'MLC_leaf_idx_File' : szFilename + "/beamlets/MLC_leaf_idx" },
            'jaw_position' : {'top_left_x_mm':echoBeam.fJawX1, 'top_left_y_mm':echoBeam.fJawY1, 'bottom_right_x_mm':echoBeam.fJawX2, 'bottom_right_y_mm':echoBeam.fJawY2 },
            'BEV_structure_contour_points_File' : szFilename + "/BEV_structure_contour_points",
            'MLC_name' :  echoBeam.szMLCName,
            'beam_modality' : echoBeam.szTechnique,
            'energy_MV' :  echoBeam.szEnergy,
            'SSD_mm' :  echoBeam.fSSD,
            'SAD_mm' :  echoBeam.fSAD,
            'influenceMatrixSparse_File' :  szFilename + "/inf_matrix_sparse",
            'influenceMatrixSparse_tol' :  fInfMatrixCutoffValue,
            'influenceMatrixFull_File' :  szFilename + "/inf_matrix_full/matrix",
            'layer_spot_indices_File' :  szFilename + "/inf_matrix_full/layer_spot_indices",
            'MLC_leaves_pos_y_mm_File' :  szFilename + "/MLC_leaves_pos_y_mm",
            'machine_name' : szMachine
                        }
        Helpers.WriteJSONFile(dctBeamData, szOuputFile)

    @staticmethod
    def WriteJSONFile(hObj, szPath):
        json_object = json.dumps(hObj, indent=4, cls=ObjectEncoder)
        # Writing to sample.json
        with open(szPath, "w") as outfile:
            outfile.write(json_object)

    @staticmethod
    def ReadDataSet(hf:h5py.File, szDataSetName:str) -> np.array :
        if(szDataSetName in hf ) :
            return hf[szDataSetName][()]
        else:
            raise Exception("Specified dataset doesn't exist")

    @staticmethod
    def CreateDataSet(hf:h5py.File, szDataSetName:str, arrData:np.array) :
        if(szDataSetName in hf ) :
            del hf[szDataSetName]
        Helpers.AddOrAppendDataSet(hf, szDataSetName, arrData)

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
    def WriteInfMatrixHDF5(arrFullDoseMatrix:np.array, doseData:DoseData, iLayerIdx, iSpotIdx, szPath) :
        bNewFile:bool = os.path.exists(szPath)

        with h5py.File(szPath, 'a') as hf:

            # write full inf matrix
            Helpers.AddOrAppendDataSet(hf, '/inf_matrix_full/matrix', arrFullDoseMatrix)
            arrLayer_Spot = np.zeros((1, 2), dtype=np.int16)
            arrLayer_Spot[0, 0] = iLayerIdx
            arrLayer_Spot[0, 1] = iSpotIdx
            Helpers.AddOrAppendDataSet(hf, '/inf_matrix_full/layer_spot_indices', arrLayer_Spot)

            # write sparse inf matrix
            lstDosePoints = doseData.dosePoints
           
            fDoseMatrixSizeX = arrFullDoseMatrix.shape[2]
            fDoseMatrixSizeY = arrFullDoseMatrix.shape[1]
            fDoseMatrixSizeZ = arrFullDoseMatrix.shape[0]
            
            iPtCnt = len(lstDosePoints)
            arrSparse = np.zeros((iPtCnt, 4))
            iSliceSize = fDoseMatrixSizeX * fDoseMatrixSizeY
            for idx in range(iPtCnt) : 
                dp:DosePoint = lstDosePoints[idx]
                arrSparse[idx, 0] = iLayerIdx
                arrSparse[idx, 1] = iSpotIdx
                arrSparse[idx, 2] = dp.sliceIndex * iSliceSize + dp.indexY * fDoseMatrixSizeX + dp.indexX
                arrSparse[idx, 3] = dp.doseValue
            Helpers.AddOrAppendDataSet(hf, '/inf_matrix_sparse', arrSparse)

class ObjectEncoder(JSONEncoder):
        def default(self, o):
            return o.__dict__
