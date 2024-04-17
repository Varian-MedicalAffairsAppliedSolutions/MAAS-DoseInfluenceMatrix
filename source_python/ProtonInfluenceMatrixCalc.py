import os
os.environ['ESAPI_PATH'] = r'C:\Program Files (x86)\Varian\RTM\16.1\esapi'
import pyesapi as pe
from array import *
import numpy as np
import datetime as dt
from inspect import getsourcefile
import h5py
import json

from DataClasses import *
from Helpers import *

def main():
    app = pe.CustomScriptExecutable.CreateApplication('ProtonInfluenecMatrixCalc')  # script name is used for logging
    patientId = '20230817'
    courseId = 'C1'
    planId = 'ProtonTestSM1' #'ProtonTest1'

    plan = Helpers.GetIonPlan(app, patientId, courseId, planId)

    plan.CalculateBeamLine()
    Helpers.SetAllSpotsToZero(plan)

    resultsDirPath = os.path.dirname(os.path.abspath(getsourcefile(lambda:0))) + '\\Results\\'
    planResultsPath = resultsDirPath + planId
    if not os.path.exists(planResultsPath) :
        os.makedirs(planResultsPath)

    Helpers.Log('Results will be written to: ' + planResultsPath)

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

                        hBeamData = Helpers.PopulateBeamData(plan, b)
                        hBeamDose = b.Dose
                        if (arrFullDoseMatrix is None):
                            arrFullDoseMatrix = np.zeros((1, hBeamDose.ZSize,hBeamDose.YSize,hBeamDose.XSize))
                        doseData = Helpers.GetNonZeroDosePoints(b.Dose, arrFullDoseMatrix)

                        Helpers.WriteBeamMetaData(hBeamData, planResultsPath)
                        Helpers.WriteResults_HDF5(hBeamData, arrFullDoseMatrix, doseData, layerIdx, spotIdx, planResultsPath)

                        #filename = b.Id + '-layer' + str(layerIdx) + '-spot' + str(spotIdx) + '-results.csv'
                        #filepath = planResultsPath + '\\' + filename
                        #Helpers.WriteResults(doseData, filepath)

                        rawSpotList[spotIdx].Weight = 0.0
                        b.ApplyParameters(hIonBeamParams)
    
    #Log.Information("Influence matrix calculation finished.");
    app.Dispose()


if __name__ == "__main__":
    main()
