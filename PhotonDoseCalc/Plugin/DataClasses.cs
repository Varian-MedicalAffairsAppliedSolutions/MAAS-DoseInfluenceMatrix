using System;
using System.Collections.Generic;
using VMS.TPS.Common.Model.Types;
using VMS.TPS.Common.Model.API;

namespace PhotonInfluenceMatrixCalc
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
    public class Beamlet
    {
        public Beamlet(int idx, string beamId)
        {
            m_iIndex = idx;
            m_szBeamId = beamId;
        }

        public Beamlet(int idx, string beamId, float xStart, float yStart, float xSize, float ySize)
        {
            m_iIndex = idx;
            m_szBeamId = beamId;
            m_fXStart = xStart;
            m_fYStart = yStart;
            m_fXSize = xSize;
            m_fYSize = ySize;
        }

        public int m_iIndex;
        public string m_szBeamId;
        public float m_fXSize;
        public float m_fYSize;
        public float m_fXStart;
        public float m_fYStart;

        public double m_dSumCutoffValues;
        public int m_iNumCutoffValues;
    }

    public class MyBeamParameters
    {
        public MyBeamParameters(VRect<double> jaws, float[,] staticLeafs)
        {
            m_rectJaws = jaws;
            m_arrStaticLeafs = staticLeafs;
            m_lstBeamlets = new List<Beamlet>();
            m_lstBeamletMLCs = new List<float[,]>();
            m_lstBeamletBeam = new List<Beam>();
            m_arrClosedMLCDoseMatrix = null;
        }
        public int BeamletCount
        {
            get { return m_lstBeamlets.Count; }
        }
        public VRect<double> m_rectJaws;
        public float[,] m_arrStaticLeafs;

        public List<Beamlet> m_lstBeamlets; // list of beamlets for this beam
        public List<float[,]> m_lstBeamletMLCs; // list of MLC position for each beamlet
        public float[,] m_ClosedMLC; // closed MLC
        public List<Beam> m_lstBeamletBeam; // beams copied from original beam. size is the same as number of beams to be calculated at a time
        public float[,] m_arrClosedMLCDoseMatrix;
    };

}
