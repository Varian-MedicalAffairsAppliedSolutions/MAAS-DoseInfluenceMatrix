using System;
using System.Collections.Generic;
using VMS.TPS.Common.Model.Types;

namespace CalculateInfluenceMatrix
{
    public class DosePoint
    {
        public DosePoint() { }
        public DosePoint(int idx, double dose)
        {
            iPtIndex = idx;
            doseValue = dose;
        }
        public int iPtIndex = 0;
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
