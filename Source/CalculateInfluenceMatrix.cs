using System;
using System.Linq;
using VMS.TPS.Common.Model.API;
using Serilog;
using System.Threading;
using System.Collections.Generic;

using System.Windows.Media.Media3D;
using VMS.TPS.Common.Model.Types;
using System.Security.Cryptography.X509Certificates;

[assembly: ESAPIScript(IsWriteable = true)]

namespace CalculateInfluenceMatrix
{
//    class ThreadData
//    {
//        IonPlanSetup m_hPlan;

//        AutoResetEvent	eventThreadExited;		// signal thread has exited
//        public ThreadData(IonPlanSetup plan)
//        {
//            m_hPlan = plan;
//            eventThreadExited = new AutoResetEvent(false);
//        }

//        IonBeam m_hBeam;
////        IonSpotParametersCollection rawSpotList;
////        IonBeamParameters m_hFieldParams;
//        int m_iFieldIdx, m_iLayerIdx, m_iSpotIdx;
////        int m_iFieldIdx, m_iFieldCnt, m_iLayerIdx, m_iLayerCnt, m_iSpotIdx, m_iSpotCnt;
////        float m_fSpotWgt;
//        string m_szOutputPath;

//        public ThreadData(IonPlanSetup plan, IonBeam hBeam, int iFieldIdx, int iLayerIdx, int iSpotIdx, string szOutputPath)
//        {
//            m_hPlan = plan;
//            m_hBeam = hBeam;
//            eventThreadExited = new AutoResetEvent(false);

//            m_iFieldIdx = iFieldIdx;
//            m_iLayerIdx = iLayerIdx;
//            m_iSpotIdx = iSpotIdx;

//            m_szOutputPath = szOutputPath;
//        }

//        public void ThreadFunc_CalcSpot2()
//        {
//            try
//            {
//                // When the raw spot list is modified above or in Helpers.SetAllSpotsToZero, the final spot list is cleared.
//                // In this case, CalculateDoseWithoutPostProcessing calculates the dose using the raw spot list.

//                CalculationResult calcRes = m_hPlan.CalculateDoseWithoutPostProcessing();
//                //if (!calcRes.Success)
//                //{
//                //    throw new ApplicationException("Dose Calculation Failed");
//                //}

//                //DoseData doseData = Helpers.GetNonZeroDosePoints(m_hPlan);
//                //string filename = string.Format("field{0}-layer{1}-spot{2}-results.csv", m_iFieldIdx, m_iLayerIdx, m_iSpotIdx);
//                //string filepath = string.Format(m_szOutputPath + "\\{0}", filename);
//                //Helpers.WriteResults(doseData, filepath);
//            }
//            catch (Exception e)
//            {
//                int i = 0;
//            }
//            finally
//            {
//                eventThreadExited.Set();
//            }
//        }
//        public void ThreadFunc_CalcSpot()
//        {
//            try
//            {
//                // When the raw spot list is modified above or in Helpers.SetAllSpotsToZero, the final spot list is cleared.
//                // In this case, CalculateDoseWithoutPostProcessing calculates the dose using the raw spot list.
//                CalculationResult calcRes = m_hPlan.CalculateDoseWithoutPostProcessing();
//                if (!calcRes.Success)
//                {
//                    throw new ApplicationException("Dose Calculation Failed");
//                }
//            }
//            catch (Exception e)
//            {
//            }
//            finally
//            {
//                eventThreadExited.Set();
//            }
//        }

//    }
    public class MyBeamParameters
    {
        public MyBeamParameters(int iLayers, List<int> lstSpots, IonBeamParameters hParams)
        {
            iLayerCnt  = iLayers;
            lstSpotCnt = lstSpots;
            hIonBeamParams = hParams;
        }
        public int iLayerCnt;
        public List<int> lstSpotCnt;
        public IonBeamParameters hIonBeamParams;
    };

    class CalculateInfluenceMatrix
    {
        // Spot weight is set to 100 so that the dose penumbra is better captured in case the "dose cut-off" setting of dose calculation is high.
        // The resulting dose values are divided by 100 before saving the results.
        public const float spotWeight = 100f;

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                Helpers.StartLogging();

                string patientId = "";
                string courseId = "";
                string planId = "";
                if (!Helpers.ParseInputArgs(args, ref patientId, ref courseId, ref planId))
                {
                    Helpers.GetPatientInfoFromUser(ref patientId, ref courseId, ref planId);
                }
                using (Application app = Application.CreateApplication())
                {
                    Execute(app, patientId, courseId, planId);
                }
            }
            catch (Exception e)
            {
                Log.Error(e.ToString());
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        static void Execute(Application app, string patientId, string courseId, string planId)
        {
            IonPlanSetup plan = Helpers.GetIonPlan(app, patientId, courseId, planId);

            plan.CalculateBeamLine();
            Helpers.SetAllSpotsToZero(plan);

            int iFieldCnt = plan.IonBeams.Count();

            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string resultsDirPath = System.IO.Path.GetDirectoryName(exePath) + "\\results";
            if (!System.IO.Directory.Exists(resultsDirPath))
            {
                System.IO.Directory.CreateDirectory(resultsDirPath);
            }
            string planResultsPath = resultsDirPath + $"\\{planId}";
            System.IO.Directory.CreateDirectory(planResultsPath);
            Log.Information($"Results will be written to: {planResultsPath}");

            // cache data for all beams
            int iMaxLayerCnt = int.MinValue;
            Dictionary<IonBeam, MyBeamParameters> tblBeamParameters = new Dictionary<IonBeam, MyBeamParameters>();
            foreach (IonBeam b in plan.IonBeams)
            {
                IonBeamParameters hParams = b.GetEditableParameters();
                IonControlPointPairCollection icpps = hParams.IonControlPointPairs;
                int iLayerCnt = icpps.Count;
                if (iMaxLayerCnt < iLayerCnt)
                    iMaxLayerCnt = iLayerCnt;

                List<int> lst = new List<int>();
                for (int layerIdx = 0; layerIdx < iLayerCnt; layerIdx++)
                    lst.Add(icpps[layerIdx].RawSpotList.Count);

                tblBeamParameters[b] = new MyBeamParameters(iLayerCnt, lst, hParams);
            }

            // find max # of spots for each layer
            List<int> lstMaxSpotCnt = new List<int>();
            for (int layerIdx = 0; layerIdx < iMaxLayerCnt; layerIdx++)
            {
                int iMaxSpotCnt = int.MinValue;
                foreach (IonBeam b in plan.IonBeams)
                {
                    MyBeamParameters bp = tblBeamParameters[b];
                    if (layerIdx < bp.iLayerCnt)
                    {
                        if (iMaxSpotCnt < bp.lstSpotCnt[layerIdx])
                            iMaxSpotCnt = bp.lstSpotCnt[layerIdx];
                    }
                }
                lstMaxSpotCnt.Add(iMaxSpotCnt);
            }

            double[,] arrFullDoseMatrix=null;

            // lopp thru layers
            for (int layerIdx = 0; layerIdx < iMaxLayerCnt; layerIdx++)
            {
                // loop thru spots
                int iMaxSpotCnt = lstMaxSpotCnt[layerIdx];
                for (int spotIdx = 0; spotIdx < iMaxSpotCnt; spotIdx++)
                {
                    bool bRunCalc = false;
                    // turn on this spot for all beams
                    foreach (IonBeam b in plan.IonBeams)
                    {
                        MyBeamParameters bp = tblBeamParameters[b];
                        if (layerIdx < bp.iLayerCnt && spotIdx < bp.lstSpotCnt[layerIdx])
                        {
                            IonSpotParametersCollection rawSpotList = bp.hIonBeamParams.IonControlPointPairs[layerIdx].RawSpotList;

                            rawSpotList[spotIdx].Weight = spotWeight;
                            b.ApplyParameters(bp.hIonBeamParams);
                            bRunCalc = true;
                        }
                    }

                    if( bRunCalc )
                    {
                        Log.Information($"Progress: Layer {layerIdx + 1} / {iMaxLayerCnt}, Spot {spotIdx + 1} / {iMaxSpotCnt}.");

                        // When the raw spot list is modified above or in Helpers.SetAllSpotsToZero, the final spot list is cleared.
                        // In this case, CalculateDoseWithoutPostProcessing calculates the dose using the raw spot list.
                        // calculate this spot for all beams
                        CalculationResult calcRes = plan.CalculateDoseWithoutPostProcessing();
                        if (!calcRes.Success)
                        {
                            throw new ApplicationException("Dose Calculation Failed");
                        }

                        // extract dose for all beams
                        foreach (IonBeam b in plan.IonBeams)
                        {
                            MyBeamParameters bp = tblBeamParameters[b];
                            if (layerIdx < bp.iLayerCnt && spotIdx < bp.lstSpotCnt[layerIdx])
                            {
                                IonSpotParametersCollection rawSpotList = bp.hIonBeamParams.IonControlPointPairs[layerIdx].RawSpotList;
                                BeamMetaData hBeamData = Helpers.PopulateBeamData(plan, b);

                                BeamDose hBeamDose = b.Dose;
                                if (arrFullDoseMatrix == null)
                                    arrFullDoseMatrix = new double[1,hBeamDose.ZSize * hBeamDose.YSize * hBeamDose.XSize];
                                DoseData doseData = Helpers.GetNonZeroDosePoints(b.Dose, ref arrFullDoseMatrix);

                                //string filename = string.Format("{0}-layer{1}-spot{2}-results.csv", b.Id, layerIdx, spotIdx);
                                //string szFilepath = string.Format(planResultsPath + "\\{0}", filename);
                                //Helpers.WriteResults_CVS(hBeamData, doseData, szFilepath);

                                Helpers.WriteResults_HDF5(hBeamData, arrFullDoseMatrix, doseData, layerIdx, spotIdx, planResultsPath);

                                rawSpotList[spotIdx].Weight = 0.0f;
                                b.ApplyParameters(bp.hIonBeamParams);
                            }
                        }
                    }
                }
            }
            Log.Information("Influence matrix calculation finished.");
        }
    }
}
