
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

class DosePoint:
    def __init__(self, x, y, slice, dose):
        self.indexX = x
        self.indexY = y
        self.sliceIndex = slice
        self.doseValue = dose

class DoseData :
    def __init__(self, lstDP:list) :
        self.dosePoints = lstDP

class MyBeamParameters :
    def __init__ (self, iLayers, lstSpots, hParams) :
        self.iLayerCnt  = iLayers
        self.lstSpotCnt = lstSpots
        self.hIonBeamParams = hParams

class BeamletInfo :
    def __init__(self, szIDFile="", iWidth=0, iHeight=0, szXPosFile="", szYPosFile="", szMLCLeafIdxFile="") :
        self.id_File = szIDFile
        self.width_mm_File = iWidth
        self.height_mm_File = iHeight
        self.position_x_mm_File = szXPosFile
        self.position_y_mm_File = szYPosFile
        self.MLC_leaf_idx_File = szMLCLeafIdxFile

class IsocenterPosition : 
    def __init__(self, x=0, y=0, z=0) :
        self.x_mm = x
        self.y_mm = y
        self.z_mm = z

class JawPosition :
    def t__init__(self, x1=0, y1=0, x2=0, y2=0) :
        self.top_left_x_mm = x1
        self.top_left_y_mm = y1
        self.bottom_right_x_mm = x2
        self.bottom_right_y_mm = y2
    def __init__(self, vrectJawPositions:pe.VRect=pe.VRect[float](0,0,0,0)) :
        self.top_left_x_mm = vrectJawPositions.X1
        self.top_left_y_mm = vrectJawPositions.Y1
        self.bottom_right_x_mm = vrectJawPositions.X2
        self.bottom_right_y_mm = vrectJawPositions.Y2    
        
# class BeamMetaData :
#     def __init__(self) :
#         self.ID = ""
#         self.gantry_angle = 0
#         self.collimator_angle = 0
#         self.couch_angle = 0
#         self.doseMatrixSizeX = 0
#         self.doseMatrixSizeY = 0
#         self.doseMatrixSizeZ = 0

#         self.iso_center = IsocenterPosition()
#         self.beamlets = BeamletInfo()
#         self.jaw_position = JawPosition()

#         self.BEV_structure_contour_points_File = ""
#         self.MLC_name = ""
#         self.beam_modality = ""
#         self.energy_MV = ""
#         self.SSD_mm = 0
#         self.SAD_mm = 0
#         self.influenceMatrixSparse_File = ""
#         self.influenceMatrixSparse_tol = 0
#         self.influenceMatrixFull_File = ""
#         self.MLC_leaves_pos_y_mm_File = ""
#         self.machine_name = ""
