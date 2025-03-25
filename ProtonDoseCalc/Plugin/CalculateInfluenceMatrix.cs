using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using CalculateInfluenceMatrix;
using System.IO;
using HDF5CSharp;
using System.Windows.Controls;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using System.Runtime.Remoting.Contexts;
using System.Windows.Threading;

// TODO: Replace the following version attributes by creating AssemblyInfo.cs. You can do this in the properties of the Visual Studio project.
[assembly: AssemblyVersion("1.0.0.1")]
[assembly: AssemblyFileVersion("1.0.0.1")]
[assembly: AssemblyInformationalVersion("1.0")]

// TODO: Uncomment the following line if the script requires write access.
[assembly: ESAPIScript(IsWriteable = true)]

namespace VMS.TPS
{
    class MyDisplayProgress : DisplayProgress
    {
        ctrlMain m_ctrlMain;
        public MyDisplayProgress(ctrlMain ctrl)
        {
            m_ctrlMain = ctrl;
        }
        public override void Message(string szMsg)
        {
            m_ctrlMain.AddMessage(szMsg);
        }
    }

    public class Script
    {
        bool m_bCloseMainWindowOnLoaded;
        ctrlMain m_ctrlMain;

        Patient m_hPatient;
        Course m_hCourse;
        IonPlanSetup m_hPlanSetup;

        public Script()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context, System.Windows.Window window)
        {
            window.Loaded += MainWindow_Loaded;

            if (context.PlanSetup == null || context.Image == null || context.Course == null || context.Patient == null)
            {
                System.Windows.MessageBox.Show("Please open a plan first.");
                m_bCloseMainWindowOnLoaded = true;
                return;
            }
            m_hPatient = context.Patient;
            m_hCourse = context.Course;
            m_hPlanSetup = context.IonPlanSetup;

            m_ctrlMain = new ctrlMain(this, window);
            window.Title = "Calculate Influence Matrix";
            window.Content = m_ctrlMain;
            window.Height = 400;
            window.Width = 600;
        }
        void MainWindow_Loaded(object sender, EventArgs e)
        {
            if (m_bCloseMainWindowOnLoaded)
                (sender as Window).Close();
        }
        public void RunInfMatrixCalc()
        {
            System.Configuration.Configuration hConfig = System.Configuration.ConfigurationManager.OpenExeConfiguration(this.GetType().Assembly.Location);
            double dInfCutoffValue = System.Convert.ToDouble(hConfig.AppSettings.Settings["InfCutoffValue"].Value);
            bool bExportFullInfMatrix = hConfig.AppSettings.Settings["ExportFullInfMatrix"].Value == "1";
            string szOutputRootFolder = hConfig.AppSettings.Settings["OutputRootFolder"].Value;

            string resultsDirPath = szOutputRootFolder + $"\\{m_hPatient.LastName}${m_hPatient.Id}";

            MyDisplayProgress hProgress = new MyDisplayProgress(m_ctrlMain);
            Calculate(m_hPatient, m_hCourse, m_hPlanSetup, bExportFullInfMatrix, dInfCutoffValue, szOutputRootFolder, hProgress);
        }
        public static void Calculate(Patient hPatient, Course course, IonPlanSetup plan, bool bExportFullInfMatrix, double dInfCutoffValue, string resultsDirPath, DisplayProgress hProgress)
        {
            if (hProgress != null)
                hProgress.Message("Influence matrix calculation started.");

            hPatient.BeginModifications();

            hProgress.Message("Calculating beam line...");
            plan.CalculateBeamLine();
            Helpers.SetAllSpotsToZero(plan);

            int iFieldCnt = plan.IonBeams.Count();

            if (!System.IO.Directory.Exists(resultsDirPath))
            {
                System.IO.Directory.CreateDirectory(resultsDirPath);
            }

            string planResultsPath = resultsDirPath + $"\\{plan.Id}";
            if (Directory.Exists(planResultsPath))
            {
                Directory.Delete(planResultsPath, true);
                System.Threading.Thread.Sleep(5000);
            }
            Directory.CreateDirectory(planResultsPath);

            if (hProgress != null)
            {
                hProgress.Message($"Results will be written to: {planResultsPath}");
                hProgress.Message("Exporting structure outlines and masks...");
            }

            ExportStructureOutlinesAndMasks(plan, planResultsPath);

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

            string szBeamPath = System.IO.Path.Combine(planResultsPath, "Beams");
            if (!Directory.Exists(szBeamPath))
                Directory.CreateDirectory(szBeamPath);

            int iMaxPointCnt = 0;

            bool bFirstDoseCalc = true;
            float[,] arrFullDoseMatrix = null;
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
                            IonControlPointPair icpp = bp.hIonBeamParams.IonControlPointPairs[layerIdx];
                            IonSpotParameters spotParams = icpp.RawSpotList[spotIdx];

                            // save spot infor for export later
                            if (bp.lstSpotId.Count > 0)
                                bp.lstSpotId.Add(bp.lstSpotId.Last() + 1); // increment spot id
                            else
                                bp.lstSpotId.Add(0);
                            bp.lstSpotXPos.Add(spotParams.X);
                            bp.lstSpotYPos.Add(spotParams.Y);
                            bp.lstSpotEnergyMeV.Add(icpp.NominalBeamEnergy);

                            spotParams.Weight = Helpers.spotWeight;
                            b.ApplyParameters(bp.hIonBeamParams);
                            bRunCalc = true;
                        }
                    }

                    if (bRunCalc)
                    {
                        if( hProgress!=null )
                            hProgress.Message($"Progress: Layer {layerIdx + 1} / {iMaxLayerCnt}, Spot {spotIdx + 1} / {iMaxSpotCnt}.");

                        // When the raw spot list is modified above or in Helpers.SetAllSpotsToZero, the final spot list is cleared.
                        // In this case, CalculateDoseWithoutPostProcessing calculates the dose using the raw spot list.
                        // calculate this spot for all beams
                        CalculationResult calcRes = plan.CalculateDoseWithoutPostProcessing();
                        if (!calcRes.Success)
                        {
                            throw new ApplicationException("Dose Calculation Failed");
                        }

                        if (bFirstDoseCalc)
                        {
                            iMaxPointCnt = ExportOptimizationVoxels(plan, planResultsPath);
                            bFirstDoseCalc = false;
                        }

                        // extract dose for all beams
                        foreach (IonBeam b in plan.IonBeams)
                        {
                            MyBeamParameters bp = tblBeamParameters[b];
                            if (layerIdx < bp.iLayerCnt && spotIdx < bp.lstSpotCnt[layerIdx])
                            {
                                IonSpotParametersCollection rawSpotList = bp.hIonBeamParams.IonControlPointPairs[layerIdx].RawSpotList;

                                BeamDose hBeamDose = b.Dose;
                                if (arrFullDoseMatrix == null)
                                    arrFullDoseMatrix = new float[hBeamDose.ZSize * hBeamDose.YSize * hBeamDose.XSize, 1];
                                Array.Clear(arrFullDoseMatrix, 0, arrFullDoseMatrix.Length);
                                DoseData doseData = Helpers.GetDosePoints(b.Dose, dInfCutoffValue, ref arrFullDoseMatrix);

                                string szHDF5DataFile = System.IO.Path.Combine(szBeamPath, $"Beam_{b.Id}_Data.h5");
                                bool bAddLastEntry = (layerIdx == bp.iLayerCnt - 1 && spotIdx == bp.lstSpotCnt[layerIdx] - 1);
                                Helpers.WriteInfMatrixHDF5(bExportFullInfMatrix, arrFullDoseMatrix, doseData, bAddLastEntry, iMaxPointCnt, bp.lstSpotId.Last(), szHDF5DataFile);

                                // due to time constraint, CVS format has not been implemented

                                //when done, turn off this spot for all beams
                                rawSpotList[spotIdx].Weight = 0.0f;
                                b.ApplyParameters(bp.hIonBeamParams);
                            }
                        }
                    }
                }
            }
            // export beam meta data
            foreach (IonBeam b in plan.IonBeams)
            {
                string szHDF5DataFile = System.IO.Path.Combine(szBeamPath, $"Beam_{b.Id}_Data.h5");
                Helpers.WriteSpotInfoHDF5(tblBeamParameters[b], szHDF5DataFile);

                string szBeamMetaDataFile = System.IO.Path.Combine(szBeamPath, $"Beam_{b.Id}_MetaData.json");
                Helpers.WriteBeamMetaData(b, tblBeamParameters[b], dInfCutoffValue, szBeamMetaDataFile);
            }
            if( hProgress!=null )
                hProgress.Message("Influence matrix calculation finished.");
        }

        public static int ExportOptimizationVoxels(IonPlanSetup hPlanSetup, string szOutputFolder)
        {
            if (!Directory.Exists(szOutputFolder))
            {
                Directory.CreateDirectory(szOutputFolder);
            }

            PlanningItemDose hPlanDose = hPlanSetup.Dose;
            int iXSize = hPlanDose.XSize;
            int iYSize = hPlanDose.YSize;
            int iZSize = hPlanDose.ZSize;
            int iPtCnt = iXSize * iYSize * iZSize;
            VVector vOrigin = hPlanDose.Origin;
            double dXRes = hPlanDose.XRes;
            double dYRes = hPlanDose.YRes;
            double dZRes = hPlanDose.ZRes;
            double[,] npPtCoords = new double[iPtCnt, 3];
            double[] npPtWeights = new double[iPtCnt];
            double dZ, dY, dX;
            int i = 0;
            for (int z = 0; z < iZSize; z++)
            {
                dZ = vOrigin.z + (z + 0.5) * dZRes;
                for (int y = 0; y < iYSize; y++)
                {
                    dY = vOrigin.y + (y + 0.5) * dYRes;
                    for (int x = 0; x < iXSize; x++)
                    {
                        dX = vOrigin.x + (x + 0.5) * dXRes;

                        npPtCoords[i, 0] = dX;
                        npPtCoords[i, 1] = dY;
                        npPtCoords[i, 2] = dZ;
                        npPtWeights[i] = 1;
                        i++;
                    }
                }
            }

            string szDataFilename = "OptimizationVoxels_Data.h5";
            string szH5Path = System.IO.Path.Combine(szOutputFolder, szDataFilename);
            long hf = Hdf5.CreateFile(szH5Path);
            Helpers.CreateDataSet<double>(hf, "/voxel_coordinate_XYZ_mm", npPtCoords);
            Helpers.CreateDataSet<double>(hf, "/voxel_weight_mm3", npPtWeights);
            Hdf5.CloseFile(hf);

            // Save meta data
            //TECHOConfig echoConfig = myECHOJob.ECHOConfig; // Adjust according to your actual implementation
            VMS.TPS.Common.Model.API.Image hCT = hPlanSetup.StructureSet.Image;
            VVector vCTOrigin = hCT.Origin;

            var dctMetaData = new
            {
                ct_origin_xyz_mm = new[] { vCTOrigin.x, vCTOrigin.y, vCTOrigin.z },
                ct_voxel_resolution_xyz_mm = new[] { hCT.XRes, hCT.YRes, hCT.ZRes },
                dose_voxel_resolution_xyz_mm = new double[] { dXRes, dYRes, dZRes },
                ct_size_xyz = new[] { hCT.XSize, hCT.YSize, hCT.ZSize },
                cal_box_xyz_start = new[] { vOrigin.x, vOrigin.y, vOrigin.z },
                cal_box_xyz_end = new[] { vOrigin.x + dXRes * iXSize, vOrigin.y + dYRes * iYSize, vOrigin.z + dZRes * iZSize },
                ct_to_dose_voxel_map_File = $"{szDataFilename}/ct_to_dose_voxel_map",
                voxel_coordinate_XYZ_mm_File = $"{szDataFilename}/voxel_coordinate_XYZ_mm",
                opt_point_cnt = iPtCnt
            };

            string szMetaDataFile = System.IO.Path.Combine(szOutputFolder, "OptimizationVoxels_MetaData.json");
            Helpers.WriteJSONFile(dctMetaData, szMetaDataFile);
            return iPtCnt;
        }

        public static void ExportStructureOutlinesAndMasks(IonPlanSetup hPlanSetup, string szOutputFolder)
        {
            string szStructOutlinesFolder = System.IO.Path.Combine(szOutputFolder, "Beams");

            if (!Directory.Exists(szStructOutlinesFolder))
            {
                Directory.CreateDirectory(szStructOutlinesFolder);
            }

            // Export structure outlines
            foreach (Beam b in hPlanSetup.Beams)
            {
                if (b.IsSetupField)
                {
                    continue;
                }

                string szH5OutlinesPath = System.IO.Path.Combine(szStructOutlinesFolder, $"Beam_{b.Id}_Data.h5");
                long fileId1 = Hdf5.CreateFile(szH5OutlinesPath);

                foreach (Structure s in hPlanSetup.StructureSet.Structures)
                {
                    try
                    {
                        Point[][] arrOutlines = b.GetStructureOutlines(s, true);
                        if (arrOutlines != null && arrOutlines.Length > 0)
                        {
                            for (int j = 0; j < arrOutlines.Length; j++)
                            {
                                Point[] points = arrOutlines[j];
                                string szDatasetName = $"/BEV_structure_contour_points/{s.Id}/Segment-{j}";
                                double[,] arrPoints = new double[points.Length, 2];
                                for (int i = 0; i < points.Length; i++)
                                {
                                    arrPoints[i, 0] = points[i].X;
                                    arrPoints[i, 1] = points[i].Y;
                                }
                                Helpers.CreateDataSet<double>(fileId1, szDatasetName, arrPoints);
                            }
                        }
                    }
                    catch (Exception) { }   // if there is any error (empty, incomplete, etc.), we skip this structure
                }
                Hdf5.CloseFile(fileId1);
            }

            // Export structure masks
            string szH5MaskPath = System.IO.Path.Combine(szOutputFolder, "StructureSet_Data.h5");
            long fileId = Hdf5.CreateFile(szH5MaskPath);
            List<object> lstAllStructsMetaData = new List<object>();

            VMS.TPS.Common.Model.API.Image hCT = hPlanSetup.StructureSet.Image;
            foreach (Structure s in hPlanSetup.StructureSet.Structures)
            {
                try
                {
                    if (s.HasSegment)
                    {
                        string szStructID = s.Id;
                        string szStandardStructName = szStructID; // dictOrganName[szStructID]

                        byte[,,] struct3DMask = Transpose<byte>(MakeSegmentMaskForStructure(hCT, s));
                        Helpers.CreateDataSet<byte>(fileId, "/" + szStructID, struct3DMask);

                        lstAllStructsMetaData.Add(new
                        {
                            name = szStandardStructName,
                            volume_cc = s.Volume,
                            dicom_structure_name = szStructID,
                            fraction_of_vol_in_calc_box = 1, // echoData.dictOrganFractionVolInCalcBox[szStructID] if szStructID in echoData.dictOrganFractionVolInCalcBox else 1,
                            structure_mask_3d_File = $"StructureSet_Data.h5/{szStandardStructName}"
                        });
                    }
                }
                catch (Exception) { }
            }
            Hdf5.CloseFile(fileId);

            string szMetaDataFile = System.IO.Path.Combine(szOutputFolder, "StructureSet_MetaData.json");
            Helpers.WriteJSONFile(lstAllStructsMetaData, szMetaDataFile);
        }
        public static byte[,,] MakeSegmentMaskForStructure(VMS.TPS.Common.Model.API.Image hCT, Structure hStruct)
        {
            if (hStruct.HasSegment)
            {
                System.Collections.BitArray pre_buffer = new System.Collections.BitArray(hCT.ZSize);
                return fill_in_profiles(hCT, hStruct, pre_buffer);
            }
            else
                throw new Exception("Structure has no segment data");
        }
        public static T[,,] Transpose<T>(T[,,] arrInput) where T : struct
        {
            int iXSize = arrInput.GetLength(2);
            int iYSize = arrInput.GetLength(1);
            int iZSize = arrInput.GetLength(0);

            T[,,] transposed = new T[iXSize, iYSize, iZSize];

            for (int z = 0; z < iZSize; z++)
            {
                for (int y = 0; y < iYSize; y++)
                {
                    for (int x = 0; x < iXSize; x++)
                    {
                        transposed[x, y, z] = arrInput[z, y, x];
                    }
                }
            }

            return transposed;
        }

        private static byte[,,] fill_in_profiles(VMS.TPS.Common.Model.API.Image hCT, Structure hStruct, System.Collections.BitArray pre_buffer) // dose_or_image, profile_fxn, row_buffer, dtype, pre_buffer= None)
        {
            int iXSize = hCT.XSize;
            int iYSize = hCT.YSize;
            int iZSize = hCT.ZSize;
            byte[,,] mask_array = new byte[iXSize, iYSize, iZSize];

            VVector z_direction = ((iZSize - 1) * hCT.ZRes) * hCT.ZDirection;
            VVector y_step = hCT.YRes * hCT.YDirection;

            VVector start_x, stop;
            for (int x = 0; x < iXSize; x++)    //) :  # scan X dimensions
            {
                start_x = hCT.Origin + ((x * hCT.XRes) * hCT.XDirection);

                for (int y = 0; y < iYSize; y++)  // # scan Y dimension
                {
                    stop = start_x + z_direction;

                    hStruct.GetSegmentProfile(start_x, stop, pre_buffer);

                    for (int z = 0; z < iZSize; z++)
                        mask_array[x, y, z] = (byte)(pre_buffer[z] ? 1 : 0);

                    start_x = start_x + y_step;
                }
            }
            return mask_array;
        }
    }
}
