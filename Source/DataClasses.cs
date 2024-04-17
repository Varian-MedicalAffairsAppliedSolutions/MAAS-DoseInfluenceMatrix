using System;
using System.Collections.Generic;
using VMS.TPS.Common.Model.Types;

namespace CalculateInfluenceMatrix
{
    public class DosePoint
    {
        public DosePoint() { }
        public DosePoint(int x, int y, int slice, double dose)
        {
            indexX = x;
            indexY = y;
            sliceIndex = slice;
            doseValue = dose;
        }
        public int indexX = 0;
        public int indexY = 0;
        public int sliceIndex = 0;
        public double doseValue = 0;
    }

    public class DoseData
    {
        public DoseData() { }
        public DoseData(List<DosePoint> points)
        {
            dosePoints = points;
        }
        public List<DosePoint> dosePoints = new List<DosePoint>();
    };

    public class BeamletInfo
    {
        public string id_File;
        public string width_mm_File;
        public string height_mm_File;
        public string position_x_mm_File;
        public string position_y_mm_File;
        public string MLC_leaf_idx_File;
    };
    public class IsocenterPosition
    {
        public IsocenterPosition(double x, double y, double z)
        {
            x_mm = x;
            y_mm = y;
            z_mm = z;
        }
        public double x_mm;
        public double y_mm;
        public double z_mm;
    }

    public class JawPosition
    {
        public JawPosition(VRect<double> vrectJawPositions)
        {
            top_left_x_mm = vrectJawPositions.X1;
            top_left_y_mm = vrectJawPositions.Y1;
            bottom_right_x_mm = vrectJawPositions.X2;
            bottom_right_y_mm = vrectJawPositions.Y2;
        }
        public double top_left_x_mm;
        public double top_left_y_mm;
        public double bottom_right_x_mm; 
        public double bottom_right_y_mm;
    }
    public class BeamMetaData
    {
        public string ID;
        public float gantry_angle;
        public float collimator_angle;
        public float couch_angle;
        public int doseMatrixSizeX;
        public int doseMatrixSizeY;
        public int doseMatrixSizeZ;

        public IsocenterPosition iso_center;
        public BeamletInfo beamlets;
        public JawPosition jaw_position;

        public string BEV_structure_contour_points_File;
        public string MLC_name;
        public string beam_modality;
        public string energy_MV;
        public float SSD_mm;
        public float SAD_mm;
        public string influenceMatrixSparse_File;
        public float influenceMatrixSparse_tol;
        public string influenceMatrixFull_File;
        public string MLC_leaves_pos_y_mm_File;
        public string machine_name;
    }
}
