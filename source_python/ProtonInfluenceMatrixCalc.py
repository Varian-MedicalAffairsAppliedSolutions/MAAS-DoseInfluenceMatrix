import os
import shutil
#add ESAPI_PATH to system environments
#os.environ['ESAPI_PATH'] = r'C:\Program Files (x86)\Varian\RTM\16.1\esapi'
import pyesapi as pe
from pyesapi import *
from array import *
import numpy as np
import datetime as dt
import time
from inspect import getsourcefile
import h5py
import json

import sys
import clr
clr.AddReference("System")
#from System.Text import StringBuilder, Array
clr.AddReference("System.Collections")
from System.Collections.Generic import List

from DataClasses import *
from Helpers import *

sys.path.append(r'D:\Projects\Python\ECHO')

sys.path.append(r"\\pisiz3echo\echo\programs\Python\DotNET_Helpers")
clr.AddReference("NETHelpers_ProdESAPI")
import NETHelpers as nh
from NETHelpers import *

def ExportOptimizationVoxels(hPlanSetup:IonPlanSetup, r3DCalcBox:Rect3D, pointCloudsW:dict, szOutputFolder:str) :
    if not os.path.exists(szOutputFolder):
        os.mkdir(szOutputFolder)

    # save optimization points
    dictOptPointCnt:dict = dict()
    iPtCnt:int = 0
    for s in pointCloudsW :
        dictOptPointCnt[s.Id] = len(pointCloudsW[s])
        iPtCnt += dictOptPointCnt[s.Id]
    dictOptPointCnt['Total'] = iPtCnt

    npPtCoords:np.array = np.empty(shape=(iPtCnt,3), dtype=float, order='C')
    npPtWeights:np.array = np.empty(shape=(iPtCnt), dtype=float, order='C')
    i:int = 0
    for s in pointCloudsW :
        lstPoints = pointCloudsW[s]
        for cp in lstPoints:
            p:pe.VVector = cp.Item1
            npPtCoords[i,0] = p.x
            npPtCoords[i,1] = p.y
            npPtCoords[i,2] = p.z
            npPtWeights[i] = cp.Item2
            i += 1

    szDataFilename:str = "OptimizationVoxels_Data.h5"
    szH5Path:str = szOutputFolder + "\\" + szDataFilename
    with h5py.File(szH5Path, 'a') as hf:
        Helpers.CreateDataSet(hf, "/voxel_coordinate_XYZ_mm", npPtCoords)
        Helpers.CreateDataSet(hf, "/voxel_weight_mm3", npPtWeights)

    # save meta data
    hCT:Image = hPlanSetup.StructureSet.Image
    vCTOrigin:pe.VVector = hCT.Origin
    dctMetaData =  { 
        "ct_origin_xyz_mm" : [vCTOrigin.x, vCTOrigin.y, vCTOrigin.z],
        "ct_voxel_resolution_xyz_mm" : [hCT.XRes, hCT.YRes, hCT.ZRes],
        "dose_voxel_resolution_xyz_mm" : [0,0,0], #[0,0,0] if echoConfig.isUniformPointSampling else [echoConfig.fUPS_XRes, echoConfig.fUPS_YRes, echoConfig.fUPS_ZRes],
        "ct_size_xyz" : [hCT.XSize, hCT.YSize, hCT.ZSize],
        "cal_box_xyz_start" : [r3DCalcBox.X, r3DCalcBox.Y, r3DCalcBox.Z],
        'cal_box_xyz_end' : [r3DCalcBox.X+r3DCalcBox.SizeX, r3DCalcBox.Y+r3DCalcBox.SizeY, r3DCalcBox.Z+r3DCalcBox.SizeZ],
        'ct_to_dose_voxel_map_File' :  szDataFilename + "/ct_to_dose_voxel_map",
        'voxel_coordinate_XYZ_mm_File' :  szDataFilename + "/voxel_coordinate_XYZ_mm"
        ,'opt_point_cnt' : dictOptPointCnt
    }
    szMetaDataFile = szOutputFolder + "\\OptimizationVoxels_MetaData.json"
    Helpers.WriteJSONFile(dctMetaData, szMetaDataFile)      

def ExportStructureOutlinesAndMasks(hPlanSetup:IonPlanSetup, szOutputFolder:str):
    szStructOutlinesFolder = szOutputFolder + "\\Beams"

    if not os.path.exists(szStructOutlinesFolder):
        os.mkdir(szStructOutlinesFolder)

    #export structure outlines
    for b in hPlanSetup.Beams :
        if (b.IsSetupField):
            continue

        szH5OutlinesPath:str = szStructOutlinesFolder + "\\Beam_" + b.Id + "_Data.h5"
        with h5py.File(szH5OutlinesPath, 'a') as hf:
            for s in hPlanSetup.StructureSet.Structures :
                arrOutlines:System.Array[System.Array[System.Windows.Point]] = b.GetStructureOutlines(s, True)
                if (arrOutlines != None and arrOutlines.Length > 0) :
                    for i in range(arrOutlines.Length) :
                        arrPoints = arrOutlines[i]
                        szDatasetName:str = "/BEV_structure_contour_points/" + s.Id + "/Segment-" + str(i)
                        iPtCnt:int = arrPoints.Length
                        npPoints:np.array = np.empty(shape=(iPtCnt,2), dtype=float, order='C')
                        for i in range(iPtCnt) :
                            npPoints[i,0] = arrPoints[i].X
                            npPoints[i,1] = arrPoints[i].Y
                                
                        Helpers.CreateDataSet(hf, szDatasetName, npPoints)

    #export structure masks
    tmp:Structure = None
    szH5MaskPath:str = szOutputFolder + "\\StructureSet_Data.h5"
    lstAllStructsMetaData:list = list()
    with h5py.File(szH5MaskPath, 'w') as hf:
        hCT:pe.Image = hPlanSetup.StructureSet.Image
        for s in hPlanSetup.StructureSet.Structures:
            if s.HasSegment :
                szStructID = s.Id
                szStandardStructName = szStructID  #dictOrganName[szStructID]
            
                struct3DMask_byte = nh.PEHelpers.MakeSegmentMaskForStructure(hCT, s)
                struct3DMask = np.transpose(to_ndarray(struct3DMask_byte, dtype=c_bool).reshape((hCT.XSize, hCT.YSize, hCT.ZSize)),(2,1,0))

                Helpers.CreateDataSet(hf, szStructID, struct3DMask)

                lstAllStructsMetaData.append( { "name" : szStandardStructName,
                                                "volume_cc" : s.Volume,
                                                "dicom_structure_name" : szStructID,
                                                # set to 1 for now, since Transfer has not been converted
                                                "fraction_of_vol_in_calc_box" : 1, #echoData.dictOrganFractionVolInCalcBox[szStructID] if szStructID in echoData.dictOrganFractionVolInCalcBox else 1,
                                                "structure_mask_3d_File" : "StructureSet_Data.h5/" + szStandardStructName } )
                
    szMetaDataFile = szOutputFolder + "\\StructureSet_MetaData.json"
    Helpers.WriteJSONFile(lstAllStructsMetaData, szMetaDataFile)
def ExportOptimizationVoxels(hPlanSetup, szOutputFolder):
    if not os.path.exists(szOutputFolder):
        os.makedirs(szOutputFolder)

    hPlanDose = hPlanSetup.Dose
    iXSize = hPlanDose.XSize
    iYSize = hPlanDose.YSize
    iZSize = hPlanDose.ZSize
    iPtCnt = iXSize * iYSize * iZSize
    vOrigin = hPlanDose.Origin
    dXRes = hPlanDose.XRes
    dYRes = hPlanDose.YRes
    dZRes = hPlanDose.ZRes

    npPtCoords = [[0] * 3 for _ in range(iPtCnt)]
    npPtWeights = [0] * iPtCnt
    i = 0
    
    for z in range(iZSize):
        dZ = vOrigin.z + (z + 0.5) * dZRes
        for y in range(iYSize):
            dY = vOrigin.y + (y + 0.5) * dYRes
            for x in range(iXSize):
                dX = vOrigin.x + (x + 0.5) * dXRes

                npPtCoords[i][0] = dX
                npPtCoords[i][1] = dY
                npPtCoords[i][2] = dZ
                npPtWeights[i] = 1
                i += 1

    szDataFilename = "OptimizationVoxels_Data.h5"
    szH5Path = os.path.join(szOutputFolder, szDataFilename)
    
    with h5py.File(szH5Path, 'w') as hf:
        hf.create_dataset('/voxel_coordinate_XYZ_mm', data=npPtCoords)
        hf.create_dataset('/voxel_weight_mm3', data=npPtWeights)

    # Save meta data
    hCT = hPlanSetup.StructureSet.Image
    vCTOrigin = hCT.Origin

    dctMetaData = {
        'ct_origin_xyz_mm': [vCTOrigin.x, vCTOrigin.y, vCTOrigin.z],
        'ct_voxel_resolution_xyz_mm': [hCT.XRes, hCT.YRes, hCT.ZRes],
        'dose_voxel_resolution_xyz_mm': [dXRes, dYRes, dZRes],
        'ct_size_xyz': [hCT.XSize, hCT.YSize, hCT.ZSize],
        'cal_box_xyz_start': [vOrigin.x, vOrigin.y, vOrigin.z],
        'cal_box_xyz_end': [vOrigin.x + dXRes * iXSize, vOrigin.y + dYRes * iYSize, vOrigin.z + dZRes * iZSize],
        'ct_to_dose_voxel_map_File': f"{szDataFilename}/ct_to_dose_voxel_map",
        'voxel_coordinate_XYZ_mm_File': f"{szDataFilename}/voxel_coordinate_XYZ_mm",
        'opt_point_cnt': iPtCnt
    }
    szMetaDataFile = os.path.join(szOutputFolder, "OptimizationVoxels_MetaData.json")
    Helpers.WriteJSONFile(dctMetaData, szMetaDataFile)
        
def main():
    app = pe.CustomScriptExecutable.CreateApplication('ProtonInfluenecMatrixCalc')  # script name is used for logging
    patientId = '20230817'
    courseId = 'C1'
    planId = 'ProtonTestSM1' #'ProtonTest1'
    fInfCutoffValue:float = 0.0
    bExportFullInfMatrix = True
    
    plan = Helpers.GetIonPlan(app, patientId, courseId, planId)

    plan.CalculateBeamLine()
    Helpers.SetAllSpotsToZero(plan)

    resultsDirPath = os.path.dirname(os.path.abspath(getsourcefile(lambda:0))) + '\\Results\\'
    planResultsPath = resultsDirPath + patientId + "\\" + planId
    if os.path.exists(planResultsPath):
        shutil.rmtree(planResultsPath)
        time.sleep(5)
    os.makedirs(planResultsPath)        

    Helpers.Log('Results will be written to: ' + planResultsPath)
    
    ExportStructureOutlinesAndMasks(plan, planResultsPath)

    #// cache data for all beams
    iMaxLayerCnt = -1000000
    tblBeamParameters = dict()
    for b in plan.IonBeams:
        hParams = b.GetEditableParameters()
        icpps = hParams.IonControlPointPairs
        iLayerCnt = icpps.Count
        if (iMaxLayerCnt < iLayerCnt) : 
            iMaxLayerCnt = iLayerCnt

        lst = list()
        for layerIdx in range(iLayerCnt) :
            lst.append(icpps[layerIdx].RawSpotList.Count)

        tblBeamParameters[b] = MyBeamParameters(iLayerCnt, lst, hParams)

    #// find max # of spots for each layer
    lstMaxSpotCnt = list()
    for layerIdx in range(iMaxLayerCnt) :
        iMaxSpotCnt = -1000000
        for b in plan.IonBeams :
            bp = tblBeamParameters[b]
            lstSpotCnt = bp.lstSpotCnt
            if (layerIdx < bp.iLayerCnt ):
                if (iMaxSpotCnt < lstSpotCnt[layerIdx]) :
                    iMaxSpotCnt = lstSpotCnt[layerIdx]
        lstMaxSpotCnt.append(iMaxSpotCnt)

    szBeamPath:str = planResultsPath + "\\Beams\\"
    if not os.path.exists(szBeamPath):
        os.makedirs(szBeamPath)

    bFirstDoseCalc:bool = True
    arrFullDoseMatrix = None

    # Calculate influence matrix
    #// lopp thru layers
    for layerIdx in range(iMaxLayerCnt) :
        #// loop thru spots
        iMaxSpotCnt = lstMaxSpotCnt[layerIdx]
        for spotIdx in range(iMaxSpotCnt) :
            bRunCalc = False
            #// turn on this spot for all beams
            for b in plan.IonBeams :
                bp = tblBeamParameters[b]
                hIonBeamParams = bp.hIonBeamParams
                if (layerIdx < bp.iLayerCnt and spotIdx < bp.lstSpotCnt[layerIdx]) :
                    icpp:pe.IonControlPointPair = bp.hIonBeamParams.IonControlPointPairs[layerIdx]
                    spotParams:pe.IonSpotParameters = icpp.RawSpotList[spotIdx]

                    # save spot infor for export later
                    if( len(bp.lstSpotId)>0 ) :
                        bp.lstSpotId.append(bp.lstSpotId[-1] + 1) # increment spot id
                    else:
                        bp.lstSpotId.append(0)
                    bp.lstSpotXPos.append(spotParams.X)
                    bp.lstSpotYPos.append(spotParams.Y)
                    bp.lstSpotEnergyMeV.append(icpp.NominalBeamEnergy);
                    
                    spotParams.Weight = spotWeight
                    b.ApplyParameters(hIonBeamParams)
                    bRunCalc = True

            if( bRunCalc ) :
                Helpers.Log('Progress: Layer ' + str(layerIdx+1) + '/' + str(iMaxLayerCnt)+ ', Spot ' + str(spotIdx+1) + '/' + str(iMaxSpotCnt))

                #// When the raw spot list is modified above or in Helpers.SetAllSpotsToZero, the final spot list is cleared.
                #// In this case, CalculateDoseWithoutPostProcessing calculates the dose using the raw spot list.
                #// calculate this spot for all beams
                calcRes = plan.CalculateDoseWithoutPostProcessing()
                if (not calcRes.Success) :
                    raise Exception('Dose Calculation Failed')
                
                if bFirstDoseCalc :
                    ExportOptimizationVoxels(plan, planResultsPath)
                    bFirstDoseCalc = False

                #// extract dose for all beams
                for b in plan.IonBeams :
                    bp:MyBeamParameters = tblBeamParameters[b]
                    hIonBeamParams = bp.hIonBeamParams
                    if (layerIdx < bp.iLayerCnt and spotIdx < bp.lstSpotCnt[layerIdx]) :
                        rawSpotList = hIonBeamParams.IonControlPointPairs[layerIdx].RawSpotList

                        hBeamDose = b.Dose
                        
                        if bExportFullInfMatrix :
                            arrFullDoseMatrix = np.zeros((hBeamDose.ZSize,hBeamDose.YSize,hBeamDose.XSize))
                            
                        doseData = Helpers.GetDosePoints(b.Dose, fInfCutoffValue, arrFullDoseMatrix)

                        szHDF5DataFile:str = szBeamPath + "Beam_" + b.Id + "_Data.h5"
                        Helpers.WriteInfMatrixHDF5(arrFullDoseMatrix, doseData, bp.lstSpotId[-1], szHDF5DataFile);

                        rawSpotList[spotIdx].Weight = 0.0
                        b.ApplyParameters(hIonBeamParams)
    # export beam meta data
    for b in plan.IonBeams :
        szHDF5DataFile:str = szBeamPath + "Beam_" + b.Id + "_Data.h5"
        Helpers.WriteSpotInfoHDF5(tblBeamParameters[b], szHDF5DataFile)

        szBeamMetaDataFile:str = szBeamPath + "Beam_" + b.Id + "_MetaData.json"
        Helpers.WriteBeamMetaData(b, tblBeamParameters[b], fInfCutoffValue, szBeamMetaDataFile);
        
    #Log.Information("Influence matrix calculation finished.");
    app.Dispose()


if __name__ == "__main__":
    main()
