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
        public DoseData(List<DosePoint> points, double dSumCutoffValues, int iNumCutoffValues)
        {
            dosePoints = points;
            m_iNumCutoffValues = iNumCutoffValues;
            m_dSumCutoffValues = dSumCutoffValues;
        }
        public List<DosePoint> dosePoints = new List<DosePoint>();
        public double m_dSumCutoffValues;
        public int m_iNumCutoffValues;
    };
}
