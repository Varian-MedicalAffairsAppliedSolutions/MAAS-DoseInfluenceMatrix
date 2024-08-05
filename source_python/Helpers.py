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
    def GetDosePoints(hDose:pe.BeamDose, fCutoffValue:float, arrFullDoseMatrix:np.array) :
        if (hDose is None) :
            raise Exception("Dose does not exist.")

        #python api seems to apply scale & intercept already, so no need to do it here like the c# version
        #dIntercept = hDose.VoxelToDoseValue(0).Dose
        #dScale = hDose.VoxelToDoseValue(1).Dose - dIntercept   

        iXSize = hDose.XSize
        iYSize = hDose.YSize
        iZSize = hDose.ZSize
        iSliceSize = iXSize*iYSize

        dSumCutOffValues:float = 0.0
        iCutOffValueCnt:int = 0
        arr3DVoxels = hDose.np_array_like() # 3D dose matrix AFTER scale and intercept are applied
        doseFromSpot = list()
        if( arrFullDoseMatrix is None ) :
            for sliceIndex in range(iZSize) : 
                doseBuffer = arr3DVoxels[:,:,sliceIndex]
                for y in range(iYSize) :
                    for x in range(iXSize) :
                        pointDose:float = float(doseBuffer[x,y]/spotWeight)
                        if (pointDose > fCutoffValue) :
                            doseFromSpot.append(DosePoint(sliceIndex*iSliceSize + y*iXSize + x, pointDose))
                        elif (pointDose>0):
                            dSumCutOffValues += pointDose
                            iCutOffValueCnt += 1
        else :
            for sliceIndex in range(iZSize) : 
                doseBuffer = arr3DVoxels[:,:,sliceIndex]
                for y in range(iYSize) :
                    for x in range(iXSize) :
                        pointDose:float = float(doseBuffer[x,y]/spotWeight)
                        if (pointDose > fCutoffValue) :
                            doseFromSpot.append(DosePoint(sliceIndex*iSliceSize + y*iXSize + x, pointDose))
                        elif (pointDose>0):
                            dSumCutOffValues += pointDose
                            iCutOffValueCnt += 1
                      
                        arrFullDoseMatrix[sliceIndex, y, x] = pointDose
        return DoseData(doseFromSpot, dSumCutOffValues, iCutOffValueCnt)

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
    def WriteSpotInfoHDF5(beamParams: MyBeamParameters, szPath: str):
        with h5py.File(szPath, 'a') as hf:
            Helpers.CreateDataSet(hf, "/spots/id", np.array(beamParams.lstSpotId))
            Helpers.CreateDataSet(hf, "/spots/position_x_mm", np.array(beamParams.lstSpotXPos))
            Helpers.CreateDataSet(hf, "/spots/position_y_mm", np.array(beamParams.lstSpotYPos))
            Helpers.CreateDataSet(hf, "/spots/energy_layer_MeV", np.array(beamParams.lstSpotEnergyMeV))
            
            szDataSetName = '/inf_matrix_full'
            if( szDataSetName in hf ) :
                arrFullMatrix = hf[szDataSetName]
                del hf[szDataSetName]
                hf[szDataSetName] = np.transpose(arrFullMatrix,(1,0))
        
    @staticmethod
    def WriteBeamMetaData(b:pe.IonBeam, beamParams:MyBeamParameters, dInfMatrixCutoffValue: float, szOutputFile: str):
        firstCP = b.ControlPoints[0]

        szBeamID = b.Id
        szFilename = f"Beam_{szBeamID}_Data.h5"
        dctBeamData = {
            "ID": szBeamID,
            "gantry_angle": float(firstCP.GantryAngle),
            "couch_angle": float(firstCP.PatientSupportAngle),
            "iso_center": {
                "x_mm": float(b.IsocenterPosition.x),
                "y_mm": float(b.IsocenterPosition.y),
                "z_mm": float(b.IsocenterPosition.z)
            },
            "spots": {
                "id_File": f"{szFilename}/spots/id",
                "position_x_mm_File": f"{szFilename}/spots/position_x_mm",
                "position_y_mm_File": f"{szFilename}/spots/position_y_mm",
                "energy_layer_MeV_File": f"{szFilename}/spots/energy_layer_MeV"
            },
            "BEV_structure_contour_points_File": f"{szFilename}/BEV_structure_contour_points",
            "beam_modality": "Proton",
            "energy_MV": b.EnergyModeDisplayName,
            "SSD_mm": b.SSD,
            "SAD_mm": 100,
            "influenceMatrixSparse_File": f"{szFilename}/inf_matrix_sparse",
            "influenceMatrixSparse_tol": dInfMatrixCutoffValue,
            "influenceMatrixFull_File": f"{szFilename}/inf_matrix_full",
            "machine_name": b.TreatmentUnit.Id
        }
        Helpers.WriteJSONFile(dctBeamData, szOutputFile)

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
    def WriteInfMatrixHDF5(arrFullDoseMatrix:np.array, doseData:DoseData, iSpotIdx, szPath) :
        bNewFile:bool = os.path.exists(szPath)
      
        with h5py.File(szPath, 'a') as hf:
            if arrFullDoseMatrix is not None :
                # write full inf matrix
                fDoseMatrixSizeX = arrFullDoseMatrix.shape[2]
                fDoseMatrixSizeY = arrFullDoseMatrix.shape[1]
                fDoseMatrixSizeZ = arrFullDoseMatrix.shape[0]
                Helpers.AddOrAppendDataSet(hf, '/inf_matrix_full', arrFullDoseMatrix.reshape((1, fDoseMatrixSizeX*fDoseMatrixSizeY*fDoseMatrixSizeZ)))
                
            # write sparse inf matrix
            lstDosePoints = doseData.dosePoints
                      
            iPtCnt = len(lstDosePoints)
            arrSparse = np.zeros((iPtCnt, 3))
            for idx in range(iPtCnt) : 
                dp:DosePoint = lstDosePoints[idx]
                arrSparse[idx, 0] = dp.iPtIndex
                arrSparse[idx, 1] = iSpotIdx
                arrSparse[idx, 2] = dp.doseValue
            Helpers.AddOrAppendDataSet(hf, '/inf_matrix_sparse', arrSparse)

class ObjectEncoder(JSONEncoder):
        def default(self, o):
            return o.__dict__
