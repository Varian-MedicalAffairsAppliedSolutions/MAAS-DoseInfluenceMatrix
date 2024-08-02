
from array import *
import numpy as np
import datetime as dt
import h5py
import pyesapi as pe

class TBeamMetaData:
    Id: str = ''
    szEnergy: str = ''
    szTechnique: str = ''
    szMLCName: str = ''
    fGantryRtn: float = 0
    fCollRtn: float = 0
    fJawX1: float = 0
    fJawX2: float = 0
    fJawY1: float = 0
    fJawY2: float = 0
    fPatientSuppAngle: float = 0
    fIsoX: float = 0
    fIsoY: float = 0
    fIsoZ: float = 0
    fSSD: float = 0
    fSAD: float = 100

class Rect3D :
    def __init__(self, X:float=0, Y:float=0, Z:float=0, SizeX:float=0, SizeY:float=0, SizeZ:float=0) :
        self.X:float = X
        self.Y:float = Y
        self.Z:float = Z
        self.SizeX:float = SizeX
        self.SizeY:float = SizeY
        self.SizeZ:float = SizeZ
        
    def Contains(self, x:float, y:float, z:float) -> bool:
        if( (x<self.X or x>self.X+self.SizeX) or 
           (y<self.Y or y>self.Y+self.SizeY) or
           (z<self.Z or z>self.Z+self.SizeZ) ) :
            return False
        else :
            return True
        
    def AddMargin(self, fMarginX:float, fMarginY:float, fMarginZ:float) :
        self.X -= fMarginX
        self.Y -= fMarginY
        self.Z -= fMarginZ
        self.SizeX += 2*fMarginX
        self.SizeY += 2*fMarginY
        self.SizeZ += 2*fMarginZ

class DosePoint():
    doseValue : float = 0
    iPtIndex : int = 0
    def __init__(self, idx, dose):
        self.iPtIndex = idx
        self.doseValue = dose

class DoseData :
    def __init__(self, lstDP:list, dSumCutoffValues, iCutoffValueCnt) :
        self.dosePoints = lstDP
        self.dSumCutoffValues = dSumCutoffValues
        self.iCutoffValueCnt = iCutoffValueCnt

class MyBeamParameters :
    def __init__ (self, iLayers, lstSpots, hParams) :
        self.iLayerCnt  = iLayers
        self.lstSpotCnt = lstSpots
        self.hIonBeamParams = hParams
        self.lstSpotId = list()
        self.lstSpotXPos = list()
        self.lstSpotYPos = list()
        self.lstSpotEnergyMeV = list()
