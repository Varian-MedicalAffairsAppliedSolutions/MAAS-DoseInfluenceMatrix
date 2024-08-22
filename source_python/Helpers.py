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
import SimpleITK as sitk
from pydicom import dcmread
from scipy.spatial import cKDTree

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
    def WriteInfMatrixHDF5(arrFullDoseMatrix:np.array, doseData:DoseData, bAddLastEntry:bool, iMaxPointCnt:int, iSpotIdx, szPath) :
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
            arrSparse = np.zeros((iPtCnt+1, 3))
            for idx in range(iPtCnt) : 
                dp:DosePoint = lstDosePoints[idx]
                arrSparse[idx, 0] = dp.iPtIndex
                arrSparse[idx, 1] = iSpotIdx
                arrSparse[idx, 2] = dp.doseValue
            if( bAddLastEntry ):
                arrSparse[iPtCnt, 0] = iMaxPointCnt - 1
                arrSparse[iPtCnt, 1] = iSpotIdx
                arrSparse[iPtCnt, 2] = 0
                
            Helpers.AddOrAppendDataSet(hf, '/inf_matrix_sparse', arrSparse)

    @staticmethod
    def read_dicom(in_dir, case):
        dicom_names = os.listdir(os.path.join(in_dir, case))
        dicom_paths = []
        for dcm in dicom_names:
            if dcm[:2] == 'CT':
                dicom_paths.append(os.path.join(in_dir, case, dcm))

        img_positions = []
        for dcm in dicom_paths:
            ds = dcmread(dcm)
            img_positions.append(ds.ImagePositionPatient[2])

        indexes = np.argsort(np.asarray(img_positions))
        dicom_names = list(np.asarray(dicom_paths)[indexes])

        reader = sitk.ImageSeriesReader()
        reader.SetFileNames(dicom_names)
        img = reader.Execute()

        return img

    @staticmethod
    def create_ct_dose_vox_map_zyx(ct: sitk.Image, points_xyz: np.ndarray, uniform: bool = False,
                                   output_folder=None, num_points=None) -> np.ndarray:
        ct_dose_map_zyx = np.ones_like(sitk.GetArrayFromImage(ct), dtype=int) * int(-1)
        if not uniform:
            for point_num, row in enumerate(points_xyz):
                curr_indx = ct.TransformPhysicalPointToIndex(row)  # X,Y,Z
                ct_dose_map_zyx[curr_indx[::-1]] = point_num  # zyx
                if point_num == num_points:  # Temp due to Bug #TODO
                    break

            mask = np.where(ct_dose_map_zyx > -1)
            z_max, z_min = np.amax(mask[0]), np.amin(mask[0])
            y_max, y_min = np.amax(mask[1]), np.amin(mask[1])
            x_max, x_min = np.amax(mask[2]), np.amin(mask[2])
            calc_box = ct_dose_map_zyx[z_min:z_max + 1, y_min:y_max + 1, x_min:x_max + 1]

            dose_vox_ind = np.where(calc_box > -1)
            no_dose_vox_ind = np.where(calc_box == -1)
            a, nearest_ind = cKDTree(np.asarray(dose_vox_ind).T).query(np.asarray(no_dose_vox_ind).T)
            calc_box[no_dose_vox_ind] = calc_box[
                dose_vox_ind[0][nearest_ind], dose_vox_ind[1][nearest_ind], dose_vox_ind[2][nearest_ind]]
            ct_dose_map_zyx[z_min:z_max + 1, y_min:y_max + 1, x_min:x_max + 1] = calc_box
            return ct_dose_map_zyx
        else:
            fname = os.path.join(output_folder, 'OptimizationVoxels_MetaData.json')
            # Opening JSON file
            f = open(fname)
            voxels_metadata = json.load(f)
            dose_res = voxels_metadata['dose_voxel_resolution_xyz_mm']
            vox_res_int = [round(b / m) for b, m in zip(dose_res, ct.GetSpacing())]
            for point_num, row in enumerate(points_xyz):
                # row is dose voxel point. to get ct point for it, go to corner point in dose voxel and add ct resolution/2
                curr_pt = (row[0] - (dose_res[0] / 2) + (ct.GetSpacing()[0] / 2),
                           row[1] - (dose_res[1] / 2) + (ct.GetSpacing()[1] / 2),
                           row[2] - (dose_res[2] / 2) + (ct.GetSpacing()[2] / 2))
                curr_indx = ct.TransformPhysicalPointToIndex(curr_pt)  # X,Y,Z

                ct_dose_map_zyx[curr_indx[2]:(curr_indx[2] + vox_res_int[2]),
                curr_indx[1]:(curr_indx[1] + vox_res_int[1]),
                curr_indx[0]:(curr_indx[0] + vox_res_int[0])] = point_num  # zyx
            return ct_dose_map_zyx
        
    @staticmethod
    def write_image(img_arr, out_dir, case, suffix, ref_ct):
        img_itk = sitk.GetImageFromArray(img_arr)
        img_itk.SetOrigin(ref_ct.GetOrigin())
        img_itk.SetSpacing(ref_ct.GetSpacing())
        img_itk.SetDirection(ref_ct.GetDirection())
        filename = os.path.join(out_dir, case + suffix)
        sitk.WriteImage(img_itk, filename)

    @staticmethod
    def load_json(file_name):
        f = open(file_name)
        json_data = json.load(f)
        f.close()
        return json_data

    @staticmethod
    def createCTDoseVoxelMap(output_folder, numPoints):
        if os.path.exists(os.path.join(output_folder, 'CT')):
            ct = Helpers.read_dicom(output_folder, 'CT')
            ct_zyx = sitk.GetArrayFromImage(ct)  # zyx
            # sitk.WriteImage(ct, os.path.join(output_folder, 'CT.nrrd'))

            with h5py.File(os.path.join(output_folder, 'CT_Data.h5'), 'w') as hf:
                hf.create_dataset("ct_hu_3d", data=ct_zyx, chunks=True, compression='gzip', compression_opts=9)
        else:
            opt_metadata = Helpers.load_json(os.path.join(output_folder, 'OptimizationVoxels_MetaData.json'))
            ct = sitk.Image(opt_metadata['ct_size_xyz'], sitk.sitkInt32)
            ct.SetOrigin(opt_metadata['ct_origin_xyz_mm'])
            ct.SetSpacing(opt_metadata['ct_voxel_resolution_xyz_mm'])
            ct.SetDirection([1, 0, 0, 0, 1, 0, 0, 0, 1])
        # creating voxel map

        filename = os.path.join(output_folder, 'OptimizationVoxels_Data.h5')

        with h5py.File(filename, "r") as f:
            # if 'ct_to_dose_voxel_map' in f:
            #     return
            if 'voxel_coordinate_XYZ_mm' in f:
                voxel_coordinate_XYZ_mm = f['voxel_coordinate_XYZ_mm'][:]

        # create ct to dose voxel map
        Helpers.Log('Creating ct to dose voxel map..')
        
        ct_dose_map_zyx = Helpers.create_ct_dose_vox_map_zyx(ct, voxel_coordinate_XYZ_mm, uniform=False,
                                                     output_folder=output_folder, num_points=numPoints)

        # consider only inside body dose voxels
        with h5py.File(os.path.join(output_folder, 'StructureSet_Data.h5'), 'r') as f:
            if 'Patient Surface' in f.keys():
                body = f['Patient Surface'][:]
            elif 'Patient Surfac38' in f.keys():
                body = f['Patient Surfac38'][:]
            else:
                body = f['BODY'][:]
        ct_dose_map_zyx = ((ct_dose_map_zyx + 1) * body) - 1

        # write ct to doze voxel map in optimization voxel data.h5
        with h5py.File(
                os.path.join(output_folder, 'OptimizationVoxels_Data.h5'), 'a') as hf:
            if 'ct_to_dose_voxel_map' in hf.keys():
                del hf['ct_to_dose_voxel_map']
            hf.create_dataset("ct_to_dose_voxel_map", data=ct_dose_map_zyx, chunks=True, compression='gzip',
                              compression_opts=9)

class ObjectEncoder(JSONEncoder):
        def default(self, o):
            return o.__dict__
