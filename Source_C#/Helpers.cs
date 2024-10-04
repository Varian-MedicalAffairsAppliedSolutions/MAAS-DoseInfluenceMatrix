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
using System.Runtime.InteropServices;

using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows.Shapes;
using HDF5DotNet;
using PureHDF;
using System.Runtime.Serialization.Formatters.Binary;

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
            int iIdxOffset = 0;
            int[,] doseBuffer = new int[iXSize, iYSize];
            List<DosePoint> doseFromSpot = new List<DosePoint>();
            for (int sliceIndex = 0; sliceIndex < iZSize; sliceIndex++)
            {
                hDose.GetVoxels(sliceIndex, doseBuffer);
                iIdxOffset = sliceIndex * iXSize * iYSize;
                for (int j = 0; j < iYSize; j++)
                {
                    for (int i = 0; i < iXSize; i++)
                    {
                        if (doseBuffer[i, j] > 0)
                        {
                            //double pointDose = plan.Dose.VoxelToDoseValue(doseBuffer[i, j]).Dose / CalculateInfluenceMatrix.spotWeight;
                            double pointDose = (doseBuffer[i, j] * dScale + dIntercept) / CalculateInfluenceMatrix.spotWeight;
                            doseFromSpot.Add(new DosePoint(iIdxOffset + j * iXSize + i, pointDose));
                        }
                    }
                }
            }
            return new DoseData(doseFromSpot, 0, 0);
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
            int iIdxOffset = 0;
            int[,] doseBuffer = new int[iXSize, iYSize];
            List<DosePoint> doseFromSpot = new List<DosePoint>();
            for (int sliceIndex = 0; sliceIndex < iZSize; sliceIndex++)
            {
                iIdxOffset = sliceIndex * iXSize * iYSize;
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

                            doseFromSpot.Add(new DosePoint(iIdxOffset + j * iXSize + i, pointDose));
                        }
                    }
                }
            }
            return new DoseData(doseFromSpot, 0, 0);
        }
        public static DoseData GetDosePoints(BeamDose hDose, double dCutoffValue, ref float[,] arrFullDoseMatrix)
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
            double dSumCutOffValues = 0;
            int iCutOffValueCnt = 0;
            int iIdxOffset = 0, iPtIndex;
            for (int sliceIndex = 0; sliceIndex < iZSize; sliceIndex++)
            {
                hDose.GetVoxels(sliceIndex, doseBuffer);
                iIdxOffset = sliceIndex * iYSize * iXSize;
                for (int j = 0; j < iYSize; j++)
                {
                    for (int i = 0; i < iXSize; i++)
                    {
                        iPtIndex = iIdxOffset + j * iXSize + i;

                        double pointDose = (doseBuffer[i, j] * dScale + dIntercept) / CalculateInfluenceMatrix.spotWeight;
                        if (pointDose > dCutoffValue)
                        {
                            doseFromSpot.Add(new DosePoint(iPtIndex, pointDose));
                        }
                        else if (pointDose > 0)
                        {
                            dSumCutOffValues += pointDose;
                            iCutOffValueCnt++;
                        }

                        arrFullDoseMatrix[iPtIndex,0] = (float)pointDose;
                    }
                }
            }
            return new DoseData(doseFromSpot, dSumCutOffValues, iCutOffValueCnt);
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

        //public static void WriteResults_CVS(TBeamMetaData hBeamData, DoseData doseData, string szPath)
        //{
        //    StringBuilder builder = new StringBuilder();
        //    string header = "sliceIndex,yIndex,xIndex,dose";
        //    builder.AppendLine(header);
        //    foreach (DosePoint dosePoint in doseData.dosePoints)
        //    {
        //        builder.AppendLine($"{dosePoint.sliceIndex},{dosePoint.indexY},{dosePoint.indexX},{dosePoint.doseValue.ToString("0.0000000")}");
        //    }
        //    File.WriteAllText(szPath, builder.ToString());
        //}


        public static void WriteBeamMetaData(IonBeam b, MyBeamParameters beamParams, double dInfMatrixCutoffValue, string szOutputFile)
        {
            ControlPoint firstCP = b.ControlPoints[0];

            string szBeamID = b.Id;
            string szFilename = $"Beam_{szBeamID}_Data.h5";
            Dictionary<string, object> dctBeamData = new Dictionary<string, object>
            {
                { "ID", szBeamID },
                { "gantry_angle", (float)firstCP.GantryAngle },
                { "couch_angle", (float)firstCP.PatientSupportAngle },
                { "iso_center", new Dictionary<string, float> {
                    { "x_mm", (float)b.IsocenterPosition.x },
                    { "y_mm", (float)b.IsocenterPosition.y },
                    { "z_mm", (float)b.IsocenterPosition.z }
                }},
                { "spots", new Dictionary<string, string> {
                    { "id_File", $"{szFilename}/spots/id" },
                    { "position_x_mm_File", $"{szFilename}/spots/position_x_mm" },
                    { "position_y_mm_File", $"{szFilename}/spots/position_y_mm" },
                    { "energy_layer_MeV_File", $"{szFilename}/spots/energy_layer_MeV" }
                }},
                { "BEV_structure_contour_points_File", $"{szFilename}/BEV_structure_contour_points" },
                { "beam_modality", "Proton" },
                { "energy_MV", b.EnergyModeDisplayName },
                { "SSD_mm", b.SSD},
                { "SAD_mm", 100},
                { "influenceMatrixSparse_File", $"{szFilename}/inf_matrix_sparse" },
                { "influenceMatrixSparse_tol", dInfMatrixCutoffValue },
                { "influenceMatrixFull_File", $"{szFilename}/inf_matrix_full" },
                { "machine_name", b.TreatmentUnit.Id }
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
        public static T[,] ConcatArrays2<T>(Array arr1, T[,] arr2) where T : struct // by columns
        {
            int iColCnt1 = arr1.GetLength(1);
            int iColCnt2 = arr2.GetLength(1);
            int iRowCnt = arr1.GetLength(0);
            int iColCnt = iColCnt1 + iColCnt2;
            T[,] arrResult = new T[iRowCnt, iColCnt];

            int iItemSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));
            int iRowSize1 = iColCnt1 * iItemSize;
            int iRowSize2 = iColCnt2 * iItemSize;
            int iRowSize = iColCnt * iItemSize;
            for (int r=0; r<iRowCnt; r++)
            {
                Buffer.BlockCopy(arr1, r * iRowSize1, arrResult, r * iRowSize, iRowSize1);
                Buffer.BlockCopy(arr2, r * iRowSize2, arrResult, r * iRowSize+ iRowSize1, iRowSize2);
                //for (int c = 0; c < iColCnt2; c++)
                //    arrResult[r, c+ iColCnt1] = arr2[r, c];

                //for (int c=0; c<iColCnt; c++)
                //{
                //    if( c<iColCnt1)
                //        arrResult[r, c] = (T)arr1.GetValue(r, c);
                //    else
                //        arrResult[r, c] = arr2[r, c-iColCnt1];
                //}
            }
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
        public static void AddOrAppendDataSet2<T>(long fileId, string szDataSetName, T[,] arrDataSet) where T : struct
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
                (bool bSuccess, Array arrExistingData) = Hdf5.ReadDataset<T>(lGroupID, szName);
                if (bSuccess)
                    arrResult = ConcatArrays2<T>(arrExistingData, arrDataSet);
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

        public static void AppendMatrixToTempFile<T>(string szTempFile, T[,] arrFullDoseMatrix) where T : struct
        {
            using (Stream stream = File.Open(szTempFile, FileMode.Append))
            {
                BinaryFormatter bformatter = new BinaryFormatter();
                bformatter.Serialize(stream, arrFullDoseMatrix);
            }

            //T[,] arrResult = arrFullDoseMatrix;
            //if( System.IO.File.Exists(szTempFile) )
            //{
            //    using (Stream stream = File.Open(szTempFile, FileMode.Open))
            //    {
            //        BinaryFormatter bformatter = new BinaryFormatter();
            //        T[,] arr1 = (T[,])bformatter.Deserialize(stream);
            //        arrResult = ConcatArrays2<T>(arr1, arrFullDoseMatrix);
            //    }
            //}

            //using (Stream stream = File.Open(szTempFile, FileMode.Create))
            //{
            //    BinaryFormatter bformatter = new BinaryFormatter();
            //    bformatter.Serialize(stream, arrResult);
            //}
        }

        public static void WriteInfMatrixHDF5(bool bExportFullInfMatrix, float[,] arrFullDoseMatrix, DoseData doseData, bool bAddLastEntry, int iMaxPointCnt, int iSpotIdx, string szPath)
        {
            long fileId;
            bool bAppend = System.IO.File.Exists(szPath);
            if (bAppend)
                fileId = Hdf5.OpenFile(szPath);
            else
                fileId = Hdf5.CreateFile(szPath);
            string szTempFile = szPath + ".tmp";
            if(bExportFullInfMatrix)
            {
                // write full inf matrix
                //Helpers.AddOrAppendDataSet2<double>(fileId, "/inf_matrix_full", arrFullDoseMatrix);
                Helpers.AppendMatrixToTempFile<float>(szTempFile, arrFullDoseMatrix);
            }

            // write sparse inf matrix
            List<DosePoint> lstDosePoints = doseData.dosePoints;
            int iPtCnt = lstDosePoints.Count;
            double[,] arrSparse = new double[(bAddLastEntry ? iPtCnt+1 : iPtCnt), 3];
            for (int i = 0; i < iPtCnt; i++)
            {
                DosePoint dp = lstDosePoints[i];
                arrSparse[i, 0] = dp.iPtIndex;
                arrSparse[i, 1] = iSpotIdx;
                arrSparse[i, 2] = dp.doseValue;
            }
            if( bAddLastEntry )
            {
                arrSparse[iPtCnt, 0] = iMaxPointCnt-1;
                arrSparse[iPtCnt, 1] = iSpotIdx;
                arrSparse[iPtCnt, 2] = 0;
            }
            Helpers.AddOrAppendDataSet<double>(fileId, "/inf_matrix_sparse", arrSparse);

            Hdf5.CloseFile(fileId);
        }
        public static void WriteSpotInfoHDF5(MyBeamParameters beamParams, string szPath)
        {
            long fileId;
            bool bAppend = System.IO.File.Exists(szPath);
            if (bAppend)
                fileId = Hdf5.OpenFile(szPath);
            else
                fileId = Hdf5.CreateFile(szPath);

            Helpers.CreateDataSet<int>(fileId, "/spots/id", beamParams.lstSpotId.ToArray());
            Helpers.CreateDataSet<float>(fileId, "/spots/position_x_mm", beamParams.lstSpotXPos.ToArray());
            Helpers.CreateDataSet<float>(fileId, "/spots/position_y_mm", beamParams.lstSpotYPos.ToArray());
            Helpers.CreateDataSet<double>(fileId, "/spots/energy_layer_MeV", beamParams.lstSpotEnergyMeV.ToArray());

            // create fullmatrix dataset from temp file
            string szTempFile = szPath + ".tmp";
            if (System.IO.File.Exists(szTempFile))
            {
                using (Stream stream = File.Open(szTempFile, FileMode.Open))
                {
                    BinaryFormatter bformatter = new BinaryFormatter();
                    float[,] arrFullDoseMatrix = null;
                    try
                    {
                        arrFullDoseMatrix = (float[,])bformatter.Deserialize(stream);
                        while (true)
                        {
                            float[,] arr1 = (float[,])bformatter.Deserialize(stream);
                            arrFullDoseMatrix = ConcatArrays2<float>(arrFullDoseMatrix, arr1);
                        }
                    }
                    catch (Exception) { }

                    Helpers.CreateDataSet<float>(fileId, "/inf_matrix_full", arrFullDoseMatrix);
                }
                System.IO.File.Delete(szTempFile);
            }

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
