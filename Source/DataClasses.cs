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


    public class TBeamMetaData
    {
        public string Id { get; set; } = "";
        public string szEnergy { get; set; } = "";
        public string szTechnique { get; set; } = "";
        public string szMLCName { get; set; } = "";
        public float fGantryRtn { get; set; } = 0;
        public float fCollRtn { get; set; } = 0;
        public float fJawX1 { get; set; } = 0;
        public float fJawX2 { get; set; } = 0;
        public float fJawY1 { get; set; } = 0;
        public float fJawY2 { get; set; } = 0;
        public float fPatientSuppAngle { get; set; } = 0;
        public float fIsoX { get; set; } = 0;
        public float fIsoY { get; set; } = 0;
        public float fIsoZ { get; set; } = 0;
        public float fSSD { get; set; } = 0;
        public float fSAD { get; set; } = 100;
    }
}
