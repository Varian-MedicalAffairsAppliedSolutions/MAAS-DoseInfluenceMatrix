import os
#add ESAPI_PATH to system environments
#os.environ['ESAPI_PATH'] = r'C:\Program Files (x86)\Varian\RTM\16.1\esapi'
import pyesapi as pe
from pyesapi import *
from array import *
import numpy as np
import datetime as dt
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

def main():
    app = pe.CustomScriptExecutable.CreateApplication('ProtonInfluenecMatrixCalc')  # script name is used for logging
    patientId = '20230817'
    courseId = 'C1'
    planId = 'ProtonTestSM1' #'ProtonTest1'
    fInfCutoffValue:float = 0.015
    
    plan = Helpers.GetIonPlan(app, patientId, courseId, planId)

    plan.CalculateBeamLine()
    Helpers.SetAllSpotsToZero(plan)

    resultsDirPath = os.path.dirname(os.path.abspath(getsourcefile(lambda:0))) + '\\Results\\'
    planResultsPath = resultsDirPath + patientId + "\\" + planId
    if not os.path.exists(planResultsPath) :
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
                    rawSpotList = hIonBeamParams.IonControlPointPairs[layerIdx].RawSpotList

                    rawSpotList[spotIdx].Weight = spotWeight
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
                
                #// extract dose for all beams
                for b in plan.IonBeams :
                    bp:MyBeamParameters = tblBeamParameters[b]
                    hIonBeamParams = bp.hIonBeamParams
                    if (layerIdx < bp.iLayerCnt and spotIdx < bp.lstSpotCnt[layerIdx]) :
                        rawSpotList = hIonBeamParams.IonControlPointPairs[layerIdx].RawSpotList

                        hBeamDose = b.Dose
                        
                        arrFullDoseMatrix = np.zeros((hBeamDose.ZSize,hBeamDose.YSize,hBeamDose.XSize))
                        doseData = Helpers.GetNonZeroDosePoints(b.Dose, arrFullDoseMatrix)

                        hBeamData = Helpers.PopulateBeamData(b)
                        Helpers.WriteResults_HDF5(hBeamData, b.TreatmentUnit.Id, fInfCutoffValue, arrFullDoseMatrix, doseData, layerIdx, spotIdx, planResultsPath)

                        #filename = b.Id + '-layer' + str(layerIdx) + '-spot' + str(spotIdx) + '-results.csv'
                        #filepath = planResultsPath + '\\' + filename
                        #Helpers.WriteResults_CVS(doseData, filepath)

                        rawSpotList[spotIdx].Weight = 0.0
                        b.ApplyParameters(hIonBeamParams)
    
    #Log.Information("Influence matrix calculation finished.");
    app.Dispose()


if __name__ == "__main__":
    main()
