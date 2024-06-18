using System;
using System.Linq;
using System.Collections.Generic;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using Serilog;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using HDF5CSharp;
using HDF.PInvoke;
using HDF5CSharp.DataTypes;
using System;
using System.Linq;
using System.Runtime.InteropServices;

using HDF.PInvoke;
using HDF5CSharp.DataTypes;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows.Shapes;
using HDF5DotNet;
using PureHDF;

namespace CalculateInfluenceMatrix
{
    static class Helpers
    {
        public static void StartLogging()
        {
            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string logDirPath = System.IO.Path.GetDirectoryName(exePath) + "\\logs";
            if (!System.IO.Directory.Exists(logDirPath))
            {
                System.IO.Directory.CreateDirectory(logDirPath);
            }
            string logFilepath = logDirPath + string.Format("\\influence-matrix-{0}.txt", DateTime.Now.ToString(@"yyyy-MM-dd@HH-mm-ss"));

            TimeSpan logFlushInterval = new TimeSpan(0, 0, 5);
            Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(logFilepath, flushToDiskInterval: logFlushInterval)
            .WriteTo.Console()
            .CreateLogger();
            Log.Information($"Log output directed to {logFilepath}");
        }

        public static bool ParseInputArgs(string[] args, ref string patientId, ref string courseId, ref string planId)
        {
            if (args.Length == 0) return false;
            if (args.Length != 3)
            {
                throw new ApplicationException($"Unexpected number of input arguments. Please enter PatientID, CourseID, and PlanID.");
            }
            patientId = args[0];
            courseId = args[1];
            planId = args[2];
            return true;
        }

        public static void GetPatientInfoFromUser(ref string patientId, ref string courseId, ref string planId)
        {
            Log.Information("Enter PatientId:");
            patientId = Console.ReadLine();
            Log.Information("Enter CourseId:");
            courseId = Console.ReadLine();
            Log.Information("Enter PlanId:");
            planId = Console.ReadLine();
        }

        public static IonPlanSetup GetIonPlan(Application app, string patientId, string courseId, string planId)
        {
            Log.Information($"Opening Patient \"{patientId}\"");
            Patient patient = app.OpenPatientById(patientId);
            if (patient is null)
            {
                throw new ApplicationException($"Could not find Patient with ID \"{patientId}\"");
            }
            patient.BeginModifications();

            Log.Information($"Opening Plan \"{courseId} / {planId}\"");
            Course course = patient.Courses.SingleOrDefault(x => x.Id.ToLower() == courseId.ToLower());
            if (course is null)
            {
                throw new ApplicationException($"Could not find Course with ID \"{courseId}\"");
            }
            IonPlanSetup plan = course.IonPlanSetups.SingleOrDefault(x => x.Id.ToLower() == planId.ToLower());
            if (plan is null)
            {
                throw new ApplicationException($"Could not find IonPlan with ID \"{planId}\"");
            }
            Log.Information($"{planId} found.");
            return plan;
        }

        public static DoseData GetNonZeroDosePoints(BeamDose hDose)
        {
            if (hDose is null)
            {
                throw new ApplicationException("Dose does not exist.");
            }

            double dIntercept = hDose.VoxelToDoseValue(0).Dose;
            double dScale = hDose.VoxelToDoseValue(1).Dose - dIntercept;
            int iXSize = hDose.XSize;
            int iYSize = hDose.YSize;
            int iZSize = hDose.ZSize;
            int[,] doseBuffer = new int[iXSize, iYSize];
            List<DosePoint> doseFromSpot = new List<DosePoint>();
            for (int sliceIndex = 0; sliceIndex < iZSize; sliceIndex++)
            {
                hDose.GetVoxels(sliceIndex, doseBuffer);
                for (int j = 0; j < iYSize; j++)
                {
                    for (int i = 0; i < iXSize; i++)
                    {
                        if (doseBuffer[i, j] > 0)
                        {
                            //double pointDose = plan.Dose.VoxelToDoseValue(doseBuffer[i, j]).Dose / CalculateInfluenceMatrix.spotWeight;
                            double pointDose = (doseBuffer[i, j] * dScale + dIntercept) / CalculateInfluenceMatrix.spotWeight;
                            doseFromSpot.Add(new DosePoint(i, j, sliceIndex, pointDose));
                        }
                    }
                }
            }
            return new DoseData(doseFromSpot);
        }

        public static DoseData GetNonZeroDosePoints(PlanningItemDose hDose)
        {
            if (hDose is null)
            {
                throw new ApplicationException("Dose does not exist.");
            }

            double dIntercept = hDose.VoxelToDoseValue(0).Dose;
            double dScale = hDose.VoxelToDoseValue(1).Dose - dIntercept;
            int iXSize = hDose.XSize;
            int iYSize = hDose.YSize;
            int iZSize = hDose.ZSize;
            int[,] doseBuffer = new int[iXSize, iYSize];
            List<DosePoint> doseFromSpot = new List<DosePoint>();
            for (int sliceIndex = 0; sliceIndex < iZSize; sliceIndex++)
            {
                hDose.GetVoxels(sliceIndex, doseBuffer);
                for (int j = 0; j < iYSize; j++)
                {
                    for (int i = 0; i < iXSize; i++)
                    {
                        if (doseBuffer[i, j] > 0)
                        {
                            //double pointDose1 = hDose.VoxelToDoseValue(doseBuffer[i, j]).Dose / CalculateInfluenceMatrix.spotWeight;
                            double pointDose = (doseBuffer[i, j] * dScale + dIntercept) / CalculateInfluenceMatrix.spotWeight;
                            //if (Math.Abs(pointDose - pointDose1) > 0.0000001)
                            //    throw new ApplicationException("dose values don't match");

                            doseFromSpot.Add(new DosePoint(i, j, sliceIndex, pointDose));
                        }
                    }
                }
            }
            return new DoseData(doseFromSpot);
        }
        public static DoseData GetNonZeroDosePoints(BeamDose hDose, ref double[,,] arrFullDoseMatrix)
        {
            if (hDose is null)
            {
                throw new ApplicationException("Dose does not exist.");
            }

            double dIntercept = hDose.VoxelToDoseValue(0).Dose;
            double dScale = hDose.VoxelToDoseValue(1).Dose - dIntercept;
            int iXSize = hDose.XSize;
            int iYSize = hDose.YSize;
            int iZSize = hDose.ZSize;
            int[,] doseBuffer = new int[iXSize, iYSize];
            List<DosePoint> doseFromSpot = new List<DosePoint>();
            for (int sliceIndex = 0; sliceIndex < iZSize; sliceIndex++)
            {
                hDose.GetVoxels(sliceIndex, doseBuffer);
                for (int j = 0; j < iYSize; j++)
                {
                    for (int i = 0; i < iXSize; i++)
                    {
                        double pointDose = 0;
                        if (doseBuffer[i, j] > 0)
                        {
                            pointDose = (doseBuffer[i, j] * dScale + dIntercept) / CalculateInfluenceMatrix.spotWeight;
                            doseFromSpot.Add(new DosePoint(i, j, sliceIndex, pointDose));
                        }
                        arrFullDoseMatrix[sliceIndex, j, i] = pointDose;
                    }
                }
            }
            return new DoseData(doseFromSpot);
        }

        public static void SetAllSpotsToZero(IonPlanSetup plan)
        {
            for (int fieldIdx = 0; fieldIdx < plan.IonBeams.Count(); fieldIdx++)
            {
                IonBeam field = plan.IonBeams.ElementAt(fieldIdx);
                IonBeamParameters fieldParams = field.GetEditableParameters();
                IonControlPointPairCollection icpps = fieldParams.IonControlPointPairs;
                for (int layerIdx = 0; layerIdx < icpps.Count(); layerIdx++)
                {
                    IonSpotParametersCollection rawSpotList = icpps[layerIdx].RawSpotList;
                    for (int spotIdx = 0; spotIdx < rawSpotList.Count; spotIdx++)
                    {
                        rawSpotList[spotIdx].Weight = 0.0F;
                    }
                }
                field.ApplyParameters(fieldParams);
            }
        }

        public static void WriteResults_CVS(TBeamMetaData hBeamData, DoseData doseData, string szPath)
        {
            StringBuilder builder = new StringBuilder();
            string header = "sliceIndex,yIndex,xIndex,dose";
            builder.AppendLine(header);
            foreach (DosePoint dosePoint in doseData.dosePoints)
            {
                builder.AppendLine($"{dosePoint.sliceIndex},{dosePoint.indexY},{dosePoint.indexX},{dosePoint.doseValue.ToString("0.0000000")}");
            }
            File.WriteAllText(szPath, builder.ToString());
        }

        public static void WriteResults_HDF5(TBeamMetaData hBeamData, string szMachine, float fInfCutoffValue, double[,,] arrFullDoseMatrix, DoseData doseData, int iLayerIdx, int iSpotIdx, string szPath)
        {
            szPath = System.IO.Path.Combine(szPath, "Beams");
            if (!Directory.Exists(szPath))
            {
                Directory.CreateDirectory(szPath);
            }

            string szBeamMetaDataFile = System.IO.Path.Combine(szPath, $"Beam_{hBeamData.Id}_MetaData.json");
            Helpers.WriteBeamMetaData(hBeamData, szMachine, fInfCutoffValue, szBeamMetaDataFile);

            string szHDF5DataFile = System.IO.Path.Combine(szPath, $"Beam_{hBeamData.Id}_Data.h5");
            Helpers.WriteInfMatrixHDF5(arrFullDoseMatrix, doseData, iLayerIdx, iSpotIdx, szHDF5DataFile);
        }

        public static TBeamMetaData PopulateBeamData(Beam hBeam)
        {
            ControlPoint firstCP = hBeam.ControlPoints[0];
            TBeamMetaData hBeamData = new TBeamMetaData();
            hBeamData.Id = hBeam.Id;
            hBeamData.szEnergy = hBeam.EnergyModeDisplayName;
            hBeamData.szTechnique = hBeam.Technique.Id;
            hBeamData.szMLCName = (hBeam.MLC == null) ? "" : hBeam.MLC.Name;
            hBeamData.fGantryRtn = (float)firstCP.GantryAngle;
            hBeamData.fCollRtn = (float)firstCP.CollimatorAngle;
            hBeamData.fPatientSuppAngle = (float)firstCP.PatientSupportAngle;
            hBeamData.fIsoX = (float)hBeam.IsocenterPosition.x;
            hBeamData.fIsoY = (float)hBeam.IsocenterPosition.y;
            hBeamData.fIsoZ = (float)hBeam.IsocenterPosition.z;
            hBeamData.fJawX1 = (float)firstCP.JawPositions.X1;
            hBeamData.fJawX2 = (float)firstCP.JawPositions.X2;
            hBeamData.fJawY1 = (float)firstCP.JawPositions.Y1;
            hBeamData.fJawY2 = (float)firstCP.JawPositions.Y2;
            return hBeamData;
        }
        public static void WriteBeamMetaData(TBeamMetaData echoBeam, string szMachine, float fInfMatrixCutoffValue, string szOutputFile)
        {
            string szBeamID = echoBeam.Id;
            string szFilename = $"Beam_{szBeamID}_Data.h5";
            Dictionary<string, object> dctBeamData = new Dictionary<string, object>
            {
                { "ID", szBeamID },
                { "gantry_angle", echoBeam.fGantryRtn },
                { "collimator_angle", echoBeam.fCollRtn },
                { "couch_angle", echoBeam.fPatientSuppAngle },
                { "iso_center", new Dictionary<string, float> {
                    { "x_mm", echoBeam.fIsoX },
                    { "y_mm", echoBeam.fIsoY },
                    { "z_mm", echoBeam.fIsoZ }
                }},
                { "beamlets", new Dictionary<string, string> {
                    { "id_File", $"{szFilename}/beamlets/id" },
                    { "width_mm_File", $"{szFilename}/beamlets/width_mm" },
                    { "height_mm_File", $"{szFilename}/beamlets/height_mm" },
                    { "position_x_mm_File", $"{szFilename}/beamlets/position_x_mm" },
                    { "position_y_mm_File", $"{szFilename}/beamlets/position_y_mm" },
                    { "MLC_leaf_idx_File", $"{szFilename}/beamlets/MLC_leaf_idx" }
                }},
                { "jaw_position", new Dictionary<string, float> {
                    { "top_left_x_mm", echoBeam.fJawX1 },
                    { "top_left_y_mm", echoBeam.fJawY1 },
                    { "bottom_right_x_mm", echoBeam.fJawX2 },
                    { "bottom_right_y_mm", echoBeam.fJawY2 }
                }},
                { "BEV_structure_contour_points_File", $"{szFilename}/BEV_structure_contour_points" },
                { "MLC_name", echoBeam.szMLCName },
                { "beam_modality", echoBeam.szTechnique },
                { "energy_MV", echoBeam.szEnergy },
                { "SSD_mm", echoBeam.fSSD },
                { "SAD_mm", echoBeam.fSAD },
                { "influenceMatrixSparse_File", $"{szFilename}/inf_matrix_sparse" },
                { "influenceMatrixSparse_tol", fInfMatrixCutoffValue },
                { "influenceMatrixFull_File", $"{szFilename}/inf_matrix_full/matrix" },
                { "layer_spot_indices_File", $"{szFilename}/inf_matrix_full/layer_spot_indices" },
                { "MLC_leaves_pos_y_mm_File", $"{szFilename}/MLC_leaves_pos_y_mm" },
                { "machine_name", szMachine }
            };
            Helpers.WriteJSONFile(dctBeamData, szOutputFile);
        }

        public static Array ConcatArrays<T>(Array arr1, Array arr2) where T : struct
        {
            int iRowCnt1 = arr1.GetLength(0);
            int iRowCnt2 = arr2.GetLength(0);
            int iRowCnt = iRowCnt1 + iRowCnt2;
            int iDim = arr1.Rank;
            long[] arrLens = new long[iDim];
            arrLens[0] = iRowCnt;
            long iRowSize = 1;
            for (int i = 1; i < iDim; i++)
            {
                arrLens[i] = arr1.GetLength(i);
                iRowSize *= arrLens[i];
            }
            Array arrResult = Array.CreateInstance(typeof(T), arrLens);

            int iItemSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));
            long iArr1Size = iRowCnt1 * iRowSize * iItemSize;
            long iArr2Size = iRowCnt2 * iRowSize * iItemSize;

            Buffer.BlockCopy(arr1, 0, arrResult, 0, (int)iArr1Size);
            Buffer.BlockCopy(arr2, 0, arrResult, (int)iArr1Size, (int)iArr2Size);
            return arrResult;
        }
        public static void CreateDataSet<T>(long fileId, string szDataSetName, Array arrDataSet) where T : struct
        {
            string[] arrTokens = szDataSetName.Split(new char[1] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            int iLen = arrTokens.Length - 1;
            List<long> lstGroups = new List<long>();
            string szGroupName = "";
            long lGroupID = fileId;
            for (int i = 0; i < iLen; i++)
            {
                szGroupName += "/" + arrTokens[i];
                lGroupID = Hdf5.CreateOrOpenGroup(fileId, szGroupName);
                lstGroups.Add(lGroupID);
            }

            string szName = arrTokens[iLen];
            Hdf5.WriteDatasetFromArray<T>(lGroupID, szName, arrDataSet);

            for (int i = iLen - 1; i >= 0; i--)
                Hdf5.CloseGroup(lstGroups[i]);
        }
        public static void AddOrAppendDataSet<T>(long fileId, string szDataSetName, Array arrDataSet) where T : struct
        {
            string[] arrTokens = szDataSetName.Split(new char[1] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            int iLen = arrTokens.Length - 1;
            List<long> lstGroups = new List<long>();
            string szGroupName = "";
            long lGroupID = fileId;
            for (int i = 0; i < iLen; i++)
            {
                szGroupName += "/" + arrTokens[i];
                lGroupID = Hdf5.CreateOrOpenGroup(fileId, szGroupName);
                lstGroups.Add(lGroupID);
            }
            string szName = arrTokens[iLen];
            System.Type t1 = typeof(T);
            bool bDatasetExists = Hdf5Utils.ItemExists(lGroupID, szName, Hdf5ElementType.Dataset);

            Array arrResult = arrDataSet;
            if (bDatasetExists)
            {
                (bool bSuccess, Array arrExistingData) = Hdf5.ReadDatasetToArray<T>(lGroupID, szName);
                if (bSuccess)
                    arrResult = ConcatArrays<T>(arrExistingData, arrDataSet);
            }
            Hdf5.WriteDatasetFromArray<T>(lGroupID, szName, arrResult);

            //using (ChunkedDataset<T> chunkedDSet = new ChunkedDataset<T>(szName, lGroupID))
            //{
            //    if (bDatasetExists)
            //    {
            //        T[,,] dsets = Hdf5.ReadDataset<T>(lGroupID, szName).result as T[,,];
            //        chunkedDSet.AppendOrCreateDataset(dsets);
            //    }
            //    chunkedDSet.AppendOrCreateDataset(arrDataSet);
            //}
            for (int i = iLen - 1; i >= 0; i--)
                Hdf5.CloseGroup(lstGroups[i]);
        }
        public static void WriteInfMatrixHDF5(double[,,] arrFullDoseMatrix, DoseData doseData, int iLayerIdx, int iSpotIdx, string szPath) 
        {
            long fileId;
            bool bAppend = System.IO.File.Exists(szPath);
            if (bAppend)
                fileId = Hdf5.OpenFile(szPath);
            else
                fileId = Hdf5.CreateFile(szPath);

            // write full inf matrix
            Helpers.CreateDataSet<double>(fileId, "/inf_matrix_full/matrix/" + iLayerIdx.ToString() + "_" + iSpotIdx.ToString(), arrFullDoseMatrix);

            int iDoseMatrixSizeX = arrFullDoseMatrix.GetLength(1);
            int iDoseMatrixSizeY = arrFullDoseMatrix.GetLength(0);

            // write sparse inf matrix
            List<DosePoint> lstDosePoints = doseData.dosePoints;
            int iPtCnt = lstDosePoints.Count;
            double[,] arrSparse = new double[iPtCnt, 4];
            int iSliceSize = iDoseMatrixSizeX * iDoseMatrixSizeY;
            for (int i = 0; i < iPtCnt; i++)
            {
                DosePoint dp = lstDosePoints[i];
                arrSparse[i, 0] = iLayerIdx;
                arrSparse[i, 1] = iSpotIdx;
                arrSparse[i, 2] = dp.sliceIndex * iSliceSize + dp.indexY * iDoseMatrixSizeX + dp.indexX;
                arrSparse[i, 3] = dp.doseValue;
            }
            Helpers.AddOrAppendDataSet<double>(fileId, "/inf_matrix_sparse", arrSparse);

            Hdf5.CloseFile(fileId);
        }
        public static void WriteJSONFile(Object hObj, string szPath)
        {
            JsonSerializer serializer = new JsonSerializer();
            serializer.Converters.Add(new JavaScriptDateTimeConverter());
            serializer.NullValueHandling = NullValueHandling.Ignore;

            using (StreamWriter sw = new StreamWriter(szPath))
            {
                using (JsonTextWriter writer = new JsonTextWriter(sw))
                {
                    writer.Formatting = Formatting.Indented;
                    writer.Indentation = 1;
                    writer.IndentChar = '\t';

                    serializer.Serialize(writer, hObj);
                }
            }
        }
    }

}
