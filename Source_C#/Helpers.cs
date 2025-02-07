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
using PureHDF.Filters;
using System.Runtime.Serialization.Formatters.Binary;
using PureHDF.VOL.Native;
using static System.Net.WebRequestMethods;
using H5S = HDF.PInvoke.H5S;

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
        //public static T[,] ConcatArrays2<T>(Array arr1, T[,] arr2) where T : struct // by columns
        //{
        //    int iColCnt1 = arr1.GetLength(1);
        //    int iColCnt2 = arr2.GetLength(1);
        //    int iRowCnt = arr1.GetLength(0);
        //    int iColCnt = iColCnt1 + iColCnt2;
        //    T[,] arrResult = new T[iRowCnt, iColCnt];

        //    int iItemSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));
        //    int iRowSize1 = iColCnt1 * iItemSize;
        //    int iRowSize2 = iColCnt2 * iItemSize;
        //    int iRowSize = iColCnt * iItemSize;
        //    for (int r = 0; r < iRowCnt; r++)
        //    {
        //        Buffer.BlockCopy(arr1, r * iRowSize1, arrResult, r * iRowSize, iRowSize1);
        //        Buffer.BlockCopy(arr2, r * iRowSize2, arrResult, r * iRowSize + iRowSize1, iRowSize2);
        //        //for (int c = 0; c < iColCnt2; c++)
        //        //    arrResult[r, c+ iColCnt1] = arr2[r, c];

        //        //for (int c=0; c<iColCnt; c++)
        //        //{
        //        //    if( c<iColCnt1)
        //        //        arrResult[r, c] = (T)arr1.GetValue(r, c);
        //        //    else
        //        //        arrResult[r, c] = arr2[r, c-iColCnt1];
        //        //}
        //    }
        //    return arrResult;
        //}

        // this function is used to do HDF - look into this later - want to do compression by modifying these functions
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
            using (ChunkedDataset<T> chunkedDSet = new ChunkedDataset<T>(szName, lGroupID))
            {
                if (bDatasetExists)
                {
                    T[,,] dsets = Hdf5.ReadDataset<T>(lGroupID, szName).result as T[,,];
                    chunkedDSet.AppendOrCreateDataset(dsets);
                }
                chunkedDSet.AppendOrCreateDataset(arrDataSet);
            }
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
            using (ChunkedDataset<T> chunkedDSet = new ChunkedDataset<T>(szName, lGroupID))
            {
                if (bDatasetExists)
                {
                    T[,,] dsets = Hdf5.ReadDataset<T>(lGroupID, szName).result as T[,,];
                    chunkedDSet.AppendOrCreateDataset(dsets);
                }
                chunkedDSet.AppendOrCreateDataset(arrDataSet);
            }
            for (int i = iLen - 1; i >= 0; i--)
                Hdf5.CloseGroup(lstGroups[i]);
        }

        //public static void AppendMatrixToTempFile<T>(string szTempFile, T[,] arrFullDoseMatrix) where T : struct
        //{
        //    using (Stream stream = System.IO.File.Open(szTempFile, FileMode.Append))
        //    {
        //        BinaryFormatter bformatter = new BinaryFormatter();
        //        bformatter.Serialize(stream, arrFullDoseMatrix);
        //    }

        //    T[,] arrResult = arrFullDoseMatrix;
        //    if (System.IO.File.Exists(szTempFile))
        //    {
        //        using (Stream stream = System.IO.File.Open(szTempFile, FileMode.Open))
        //        {
        //            BinaryFormatter bformatter = new BinaryFormatter();
        //            T[,] arr1 = (T[,])bformatter.Deserialize(stream);
        //            arrResult = ConcatArrays2<T>(arr1, arrFullDoseMatrix);
        //        }
        //    }

        //    using (Stream stream = System.IO.File.Open(szTempFile, FileMode.Create))
        //    {
        //        BinaryFormatter bformatter = new BinaryFormatter();
        //        bformatter.Serialize(stream, arrResult);
        //    }
        //}

        //// Adding in compressor

        //public static void WriteInfMatrixHDF5(bool bExportFullInfMatrix, float[,] arrFullDoseMatrix, DoseData doseData, bool bAddLastEntry, int iMaxPointCnt, int iSpotIdx, string szPath)
        //{
        //    long fileId;
        //    bool bAppend = System.IO.File.Exists(szPath);
        //    if (bAppend)
        //        fileId = Hdf5.OpenFile(szPath);
        //    else
        //        fileId = Hdf5.CreateFile(szPath);
        //    string szTempFile = szPath + ".tmp";
        //    if(bExportFullInfMatrix)
        //    {
        //        // write full inf matrix
        //        //Helpers.AddOrAppendDataSet2<double>(fileId, "/inf_matrix_full", arrFullDoseMatrix);
        //        Helpers.AppendMatrixToTempFile<float>(szTempFile, arrFullDoseMatrix);
        //    }

        //    // write sparse inf matrix
        //    List<DosePoint> lstDosePoints = doseData.dosePoints;
        //    int iPtCnt = lstDosePoints.Count;
        //    double[,] arrSparse = new double[(bAddLastEntry ? iPtCnt+1 : iPtCnt), 3];
        //    for (int i = 0; i < iPtCnt; i++)
        //    {
        //        DosePoint dp = lstDosePoints[i];
        //        arrSparse[i, 0] = dp.iPtIndex;
        //        arrSparse[i, 1] = iSpotIdx;
        //        arrSparse[i, 2] = dp.doseValue;
        //    }
        //    if( bAddLastEntry )
        //    {
        //        arrSparse[iPtCnt, 0] = iMaxPointCnt-1;
        //        arrSparse[iPtCnt, 1] = iSpotIdx;
        //        arrSparse[iPtCnt, 2] = 0;
        //    }
        //    Helpers.AddOrAppendDataSet<double>(fileId, "/inf_matrix_sparse", arrSparse);

        //    Hdf5.CloseFile(fileId);

        //}


        private static void WriteCompressedMatrix<T>(long fileId, string datasetPath, T[,] matrix, bool isFirstSpot) where T : struct
        {
            long dataspaceId = -1;
            long dcpl = -1;
            long datasetId = -1;
            GCHandle handle = default;


            try
            {
                // Get dimensions for dataset
                ulong[] dims = new ulong[] {
            (ulong)matrix.GetLength(0),    // Number of voxels
            1                              // Start with one column for first spot
        };

                // For the first spot, we need to create the dataset with extensible dimensions
                if (isFirstSpot)
                {
                    // Create dataspace with maximum dimensions
                    // H5S.UNLIMITED allows infinite extension in the second dimension
                    ulong[] maxDims = new ulong[] {
                (ulong)matrix.GetLength(0),    // Fixed number of voxels
                HDF.PInvoke.H5S.UNLIMITED     // Unlimited spots
            };

                    dataspaceId = HDF.PInvoke.H5S.create_simple(2, dims, maxDims);
                    if (dataspaceId < 0) throw new Exception("Failed to create dataspace");

                    // Create property list for dataset creation
                    dcpl = HDF.PInvoke.H5P.create(HDF.PInvoke.H5P.DATASET_CREATE);
                    if (dcpl < 0) throw new Exception("Failed to create property list");

                    // Set chunk size for efficient expansion and compression
                    ulong[] chunkDims = new ulong[] {
                (ulong)Math.Min(matrix.GetLength(0), 1024), // Chunk height
                1                                           // One spot per chunk
            };

                    if (HDF.PInvoke.H5P.set_chunk(dcpl, 2, chunkDims) < 0)
                        throw new Exception("Failed to set chunk size");

                    // Enable compression
                    if (HDF.PInvoke.H5P.set_shuffle(dcpl) < 0)
                        throw new Exception("Failed to set shuffle filter");
                    if (HDF.PInvoke.H5P.set_deflate(dcpl, 9) < 0)
                        throw new Exception("Failed to set compression");

                    // Select appropriate data type
                    long datatype = typeof(T) == typeof(float) ?
                        HDF.PInvoke.H5T.NATIVE_FLOAT :
                        HDF.PInvoke.H5T.NATIVE_DOUBLE;

                    // Create the extensible dataset
                    datasetId = HDF.PInvoke.H5D.create(fileId, datasetPath, datatype, dataspaceId,
                        HDF.PInvoke.H5P.DEFAULT, dcpl, HDF.PInvoke.H5P.DEFAULT);

                    if (datasetId < 0) throw new Exception("Failed to create dataset");

                    Log.Information($"Created new extensible dataset: {datasetPath}");
                }
                else
                {
                    // Open existing dataset
                    datasetId = HDF.PInvoke.H5D.open(fileId, datasetPath);
                    if (datasetId < 0) throw new Exception($"Failed to open dataset: {datasetPath}");

                    // Get current dimensions
                    long spaceId = HDF.PInvoke.H5D.get_space(datasetId);
                    ulong[] currentDims = new ulong[2];
                    HDF.PInvoke.H5S.get_simple_extent_dims(spaceId, currentDims, null);
                    HDF.PInvoke.H5S.close(spaceId);

                    // Extend dataset by one column
                    ulong[] newDims = new ulong[] { currentDims[0], currentDims[1] + 1 };
                    if (HDF.PInvoke.H5D.set_extent(datasetId, newDims) < 0)
                        throw new Exception("Failed to extend dataset");

                    Log.Information($"Extended dataset to {newDims[1]} spots");
                }

                // Write the data
                handle = GCHandle.Alloc(matrix, GCHandleType.Pinned);
                IntPtr buf = handle.AddrOfPinnedObject();

                if (!isFirstSpot)
                {
                    // For subsequent spots, write to the last column
                    long memspace = HDF.PInvoke.H5S.create_simple(2, dims, null);
                    long filespace = HDF.PInvoke.H5D.get_space(datasetId);

                    ulong[] currentDims = new ulong[2];
                    HDF.PInvoke.H5S.get_simple_extent_dims(filespace, currentDims, null);

                    // Select the last column for writing
                    ulong[] start = new ulong[] { 0, currentDims[1] - 1 };
                    ulong[] count = new ulong[] { dims[0], 1 };
                    HDF.PInvoke.H5S.select_hyperslab(filespace, H5S.seloper_t.SET, start, null, count, null);

                    if (HDF.PInvoke.H5D.write(datasetId, HDF.PInvoke.H5T.NATIVE_FLOAT, memspace, filespace,
                        HDF.PInvoke.H5P.DEFAULT, buf) < 0)
                    {
                        throw new Exception("Failed to write data to dataset");
                    }

                    HDF.PInvoke.H5S.close(memspace);
                    HDF.PInvoke.H5S.close(filespace);
                }
                else
                {
                    // For first spot, we can write directly
                    if (HDF.PInvoke.H5D.write(datasetId, HDF.PInvoke.H5T.NATIVE_FLOAT,
                        HDF.PInvoke.H5S.ALL, HDF.PInvoke.H5S.ALL, HDF.PInvoke.H5P.DEFAULT, buf) < 0)
                    {
                        throw new Exception("Failed to write initial data to dataset");
                    }
                }

                Log.Information($"Successfully wrote data to {datasetPath}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error writing compressed matrix: {ex.Message}");
                throw;
            }
            finally
            {
                if (handle.IsAllocated)
                    handle.Free();
                if (datasetId >= 0)
                    HDF.PInvoke.H5D.close(datasetId);
                if (dataspaceId >= 0)
                    HDF.PInvoke.H5S.close(dataspaceId);
                if (dcpl >= 0)
                    HDF.PInvoke.H5P.close(dcpl);
            }
        }

        public static void WriteInfMatrixHDF5(bool bExportFullInfMatrix, float[,] arrFullDoseMatrix,
            DoseData doseData, bool bAddLastEntry, int iMaxPointCnt, int iSpotIdx, string szPath)
        {
            long fileId = -1;
            try
            {
                Log.Information($"Starting to write matrix to {szPath}");

                // First, handle file creation/opening
                if (!System.IO.File.Exists(szPath))
                {
                    fileId = HDF.PInvoke.H5F.create(szPath, HDF.PInvoke.H5F.ACC_TRUNC);
                    Log.Information("Created new HDF5 file");
                }
                else
                {
                    fileId = HDF.PInvoke.H5F.open(szPath, HDF.PInvoke.H5F.ACC_RDWR);
                    Log.Information("Opened existing HDF5 file");
                }

                if (fileId < 0)
                    throw new Exception("Failed to create/open HDF5 file");

                // Handle full matrix if requested
                if (bExportFullInfMatrix)
                {
                    bool fullMatrixExists = HDF.PInvoke.H5L.exists(fileId, "/inf_matrix_full") > 0;
                    Log.Information($"Full matrix dataset {(fullMatrixExists ? "exists" : "does not exist")}");

                    if (!fullMatrixExists)
                    {
                        CreateInitialDataset(fileId, "/inf_matrix_full", arrFullDoseMatrix);
                    }
                    else
                    {
                        AppendToDataset(fileId, "/inf_matrix_full", arrFullDoseMatrix);
                    }
                }

                // Now handle sparse matrix
                if (doseData?.dosePoints != null)
                {
                    // Convert dose points to matrix format
                    int iPtCnt = doseData.dosePoints.Count;
                    double[,] arrSparse = new double[(bAddLastEntry ? iPtCnt + 1 : iPtCnt), 3];

                    // Fill sparse matrix data
                    for (int i = 0; i < iPtCnt; i++)
                    {
                        DosePoint dp = doseData.dosePoints[i];
                        arrSparse[i, 0] = dp.iPtIndex;
                        arrSparse[i, 1] = iSpotIdx;
                        arrSparse[i, 2] = dp.doseValue;
                    }

                    if (bAddLastEntry)
                    {
                        arrSparse[iPtCnt, 0] = iMaxPointCnt - 1;
                        arrSparse[iPtCnt, 1] = iSpotIdx;
                        arrSparse[iPtCnt, 2] = 0;
                        Log.Information("Added last entry to sparse matrix");
                    }

                    // Check if sparse matrix dataset exists
                    bool sparseMatrixExists = HDF.PInvoke.H5L.exists(fileId, "/inf_matrix_sparse") > 0;
                    Log.Information($"Sparse matrix dataset {(sparseMatrixExists ? "exists" : "does not exist")}");

                    if (!sparseMatrixExists)
                    {
                        CreateInitialDataset(fileId, "/inf_matrix_sparse", arrSparse);
                    }
                    else
                    {
                        AppendToDataset(fileId, "/inf_matrix_sparse", arrSparse);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error in WriteInfMatrixHDF5: {ex.Message}");
                Log.Error($"Stack trace: {ex.StackTrace}");
                throw;
            }
            finally
            {
                if (fileId >= 0)
                {
                    HDF.PInvoke.H5F.close(fileId);
                    Log.Information("Successfully closed HDF5 file");
                }
            }
        }

        // Modified to accept dataset path as parameter
        private static void CreateInitialDataset(long fileId, string datasetPath, Array initialData)
        {
            long dataspaceId = -1;
            long dcpl = -1;
            long datasetId = -1;

            try
            {
                // First, determine if this is a sparse matrix by checking dimensions
                bool isSparseMatrix = initialData.GetLength(1) > 1;

                // Create initial dimensions based on the data
                ulong[] dims = new ulong[] {
            (ulong)initialData.GetLength(0),
            (ulong)initialData.GetLength(1)
        };

                // Here's the crucial part - we set maximum dimensions differently for sparse vs full
                ulong[] maxDims = new ulong[] {
            isSparseMatrix ? HDF.PInvoke.H5S.UNLIMITED : dims[0],  // Allow rows to grow for sparse
            isSparseMatrix ? dims[1] : HDF.PInvoke.H5S.UNLIMITED   // Allow columns to grow for full
        };

                // Create dataspace with extensible dimensions
                dataspaceId = HDF.PInvoke.H5S.create_simple(2, dims, maxDims);
                if (dataspaceId < 0) throw new Exception("Failed to create dataspace");

                // Set up chunking and compression
                dcpl = HDF.PInvoke.H5P.create(HDF.PInvoke.H5P.DATASET_CREATE);
                if (dcpl < 0) throw new Exception("Failed to create property list");

                // Choose chunk size based on matrix type
                ulong[] chunkDims;
                if (isSparseMatrix)
                {
                    // For sparse matrix, chunk by rows
                    chunkDims = new ulong[] { 1000, dims[1] };
                }
                else
                {
                    // For full matrix, chunk by columns
                    chunkDims = new ulong[] { dims[0], 1 };
                }

                if (HDF.PInvoke.H5P.set_chunk(dcpl, 2, chunkDims) < 0)
                    throw new Exception("Failed to set chunk size");

                // Enable compression
                if (HDF.PInvoke.H5P.set_shuffle(dcpl) < 0)
                    throw new Exception("Failed to set shuffle filter");
                if (HDF.PInvoke.H5P.set_deflate(dcpl, 9) < 0)
                    throw new Exception("Failed to set compression");

                // Create dataset with appropriate data type
                long datatype = initialData is float[,]?
                    HDF.PInvoke.H5T.NATIVE_FLOAT :
                    HDF.PInvoke.H5T.NATIVE_DOUBLE;

                datasetId = HDF.PInvoke.H5D.create(fileId, datasetPath, datatype, dataspaceId,
                    HDF.PInvoke.H5P.DEFAULT, dcpl, HDF.PInvoke.H5P.DEFAULT);

                if (datasetId < 0)
                    throw new Exception("Failed to create dataset");

                // Write initial data
                GCHandle handle = GCHandle.Alloc(initialData, GCHandleType.Pinned);
                try
                {
                    if (HDF.PInvoke.H5D.write(datasetId, datatype,
                        HDF.PInvoke.H5S.ALL, HDF.PInvoke.H5S.ALL,
                        HDF.PInvoke.H5P.DEFAULT, handle.AddrOfPinnedObject()) < 0)
                    {
                        throw new Exception("Failed to write initial data");
                    }
                }
                finally
                {
                    if (handle.IsAllocated)
                        handle.Free();
                }

                Log.Information($"Successfully created extensible dataset: {datasetPath}");
            }
            finally
            {
                // Cleanup
                if (datasetId >= 0) HDF.PInvoke.H5D.close(datasetId);
                if (dataspaceId >= 0) HDF.PInvoke.H5S.close(dataspaceId);
                if (dcpl >= 0) HDF.PInvoke.H5P.close(dcpl);
            }
        }



        //       private static void AppendToDataset(long fileId, string datasetPath, Array newData)
        //{
        //    // Initialize all our HDF5 identifiers to -1 (invalid)
        //    // This helps us track what needs cleanup in the finally block
        //    long datasetId = -1;
        //    long memspace = -1;
        //    long filespace = -1;
        //    GCHandle handle = default;

        //    try
        //    {
        //        // First determine if we're dealing with a sparse or full matrix
        //        // Sparse matrices have 3 columns (point index, spot index, dose value)
        //        // Full matrices have 1 column (just the dose value)
        //        bool isSparseMatrix = newData.GetLength(1) > 1;

        //        // Open the existing dataset that we want to append to
        //        datasetId = HDF.PInvoke.H5D.open(fileId, datasetPath);
        //        if (datasetId < 0)
        //            throw new Exception($"Failed to open dataset: {datasetPath}");

        //        // Get the current dimensions of our dataset
        //        filespace = HDF.PInvoke.H5D.get_space(datasetId);
        //        ulong[] currentDims = new ulong[2];
        //        ulong[] maxDims = new ulong[2];
        //        HDF.PInvoke.H5S.get_simple_extent_dims(filespace, currentDims, maxDims);

        //        // We're done with this filespace, close it before creating a new one
        //        HDF.PInvoke.H5S.close(filespace);
        //        filespace = -1;  // Mark as closed

        //        // Calculate new dimensions based on matrix type
        //        ulong[] newDims;
        //        if (isSparseMatrix)
        //        {
        //            // For sparse matrix, we add rows (each spot has variable number of points)
        //            newDims = new ulong[] {
        //                currentDims[0] + (ulong)newData.GetLength(0),  // Add new rows
        //                3  // Keep 3 columns (fixed)
        //            };
        //        }
        //        else
        //        {
        //            // For full matrix, we add a column (one new spot)
        //            newDims = new ulong[] {
        //                currentDims[0],  // Keep same number of rows
        //                currentDims[1] + 1  // Add one column
        //            };
        //        }

        //        // Extend the dataset to accommodate new data
        //        if (HDF.PInvoke.H5D.set_extent(datasetId, newDims) < 0)
        //            throw new Exception("Failed to extend dataset");

        //        // Get the new filespace after extending
        //        filespace = HDF.PInvoke.H5D.get_space(datasetId);

        //        // Create memory space describing our new data's shape
        //        ulong[] memDims = new ulong[] {
        //            (ulong)newData.GetLength(0),
        //            (ulong)newData.GetLength(1)
        //        };
        //        memspace = HDF.PInvoke.H5S.create_simple(2, memDims, null);

        //        // Calculate where in the dataset we should write
        //        ulong[] start;
        //        if (isSparseMatrix)
        //        {
        //            // For sparse matrix, start at the end of existing rows
        //            start = new ulong[] { currentDims[0], 0 };
        //        }
        //        else
        //        {
        //            // For full matrix, start at the new column
        //            start = new ulong[] { 0, currentDims[1] };
        //        }

        //        // Select the portion of the file where we'll write
        //        if (HDF.PInvoke.H5S.select_hyperslab(filespace, H5S.seloper_t.SET, 
        //            start, null, memDims, null) < 0)
        //        {
        //            throw new Exception("Failed to select hyperslab");
        //        }

        //        // Pin our data so it doesn't move during the write operation
        //        handle = GCHandle.Alloc(newData, GCHandleType.Pinned);
        //        IntPtr buf = handle.AddrOfPinnedObject();

        //        // Determine the correct data type for our data
        //        long datatype = newData is float[,] ? 
        //            HDF.PInvoke.H5T.NATIVE_FLOAT : 
        //            HDF.PInvoke.H5T.NATIVE_DOUBLE;

        //        // Write the new data
        //        if (HDF.PInvoke.H5D.write(datasetId, datatype, memspace, filespace, 
        //            HDF.PInvoke.H5P.DEFAULT, buf) < 0)
        //        {
        //            throw new Exception("Failed to write data");
        //        }

        //        Log.Information($"Successfully appended data to {datasetPath}");
        //    }
        //    catch (Exception ex)
        //    {
        //        Log.Error($"Error in AppendToDataset: {ex.Message}");
        //        throw;
        //    }
        //    finally
        //    {
        //        // Clean up all HDF5 resources in reverse order of creation
        //        if (handle.IsAllocated)
        //            handle.Free();
        //        if (memspace >= 0)
        //            HDF.PInvoke.H5S.close(memspace);
        //        if (filespace >= 0)
        //            HDF.PInvoke.H5S.close(filespace);
        //        if (datasetId >= 0)
        //            HDF.PInvoke.H5D.close(datasetId);
        //    }
        //}

        private static void AppendToDataset(long fileId, string datasetPath, Array newData)
        {
            // Initialize all our HDF5 identifiers to -1 (invalid)
            // This helps us track what needs cleanup in the finally block
            long datasetId = -1;
            long memspace = -1;
            long filespace = -1;
            GCHandle handle = default;

            try
            {
                // First determine if we're dealing with a sparse or full matrix
                // Sparse matrices have 3 columns (point index, spot index, dose value)
                // Full matrices have 1 column (just the dose value)
                bool isSparseMatrix = newData.GetLength(1) > 1;

                // Open the existing dataset that we want to append to
                datasetId = HDF.PInvoke.H5D.open(fileId, datasetPath);
                if (datasetId < 0)
                    throw new Exception($"Failed to open dataset: {datasetPath}");

                // Get the current dimensions of our dataset
                filespace = HDF.PInvoke.H5D.get_space(datasetId);
                ulong[] currentDims = new ulong[2];
                ulong[] maxDims = new ulong[2];
                HDF.PInvoke.H5S.get_simple_extent_dims(filespace, currentDims, maxDims);

                // We're done with this filespace, close it before creating a new one
                HDF.PInvoke.H5S.close(filespace);
                filespace = -1;  // Mark as closed

                // Calculate new dimensions based on matrix type
                ulong[] newDims;
                if (isSparseMatrix)
                {
                    // For sparse matrix, we add rows (each spot has variable number of points)
                    newDims = new ulong[] {
                currentDims[0] + (ulong)newData.GetLength(0),  // Add new rows
                3  // Keep 3 columns (fixed)
            };
                }
                else
                {
                    // For full matrix, we add a column (one new spot)
                    newDims = new ulong[] {
                currentDims[0],  // Keep same number of rows
                currentDims[1] + 1  // Add one column
            };
                }

                // Extend the dataset to accommodate new data
                if (HDF.PInvoke.H5D.set_extent(datasetId, newDims) < 0)
                    throw new Exception("Failed to extend dataset");

                // Get the new filespace after extending
                filespace = HDF.PInvoke.H5D.get_space(datasetId);

                // Create memory space describing our new data's shape
                ulong[] memDims = new ulong[] {
            (ulong)newData.GetLength(0),
            (ulong)newData.GetLength(1)
        };
                memspace = HDF.PInvoke.H5S.create_simple(2, memDims, null);

                // Calculate where in the dataset we should write
                ulong[] start;
                if (isSparseMatrix)
                {
                    // For sparse matrix, start at the end of existing rows
                    start = new ulong[] { currentDims[0], 0 };
                }
                else
                {
                    // For full matrix, start at the new column
                    start = new ulong[] { 0, currentDims[1] };
                }

                // Select the portion of the file where we'll write
                if (HDF.PInvoke.H5S.select_hyperslab(filespace, H5S.seloper_t.SET,
                    start, null, memDims, null) < 0)
                {
                    throw new Exception("Failed to select hyperslab");
                }

                // Pin our data so it doesn't move during the write operation
                handle = GCHandle.Alloc(newData, GCHandleType.Pinned);
                IntPtr buf = handle.AddrOfPinnedObject();

                // Determine the correct data type for our data
                long datatype = newData is float[,]?
                    HDF.PInvoke.H5T.NATIVE_FLOAT :
                    HDF.PInvoke.H5T.NATIVE_DOUBLE;

                // Write the new data
                if (HDF.PInvoke.H5D.write(datasetId, datatype, memspace, filespace,
                    HDF.PInvoke.H5P.DEFAULT, buf) < 0)
                {
                    throw new Exception("Failed to write data");
                }

                Log.Information($"Successfully appended data to {datasetPath}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error in AppendToDataset: {ex.Message}");
                throw;
            }
            finally
            {
                // Clean up all HDF5 resources in reverse order of creation
                if (handle.IsAllocated)
                    handle.Free();
                if (memspace >= 0)
                    HDF.PInvoke.H5S.close(memspace);
                if (filespace >= 0)
                    HDF.PInvoke.H5S.close(filespace);
                if (datasetId >= 0)
                    HDF.PInvoke.H5D.close(datasetId);
            }
        }
        public static void AppendMatrixToTempFile<T>(string szTempFile, T[,] arrFullDoseMatrix) where T : struct
        {
            // Check if this is the first write to the temp file
            bool isFirstSpot = !System.IO.File.Exists(szTempFile);

            long fileId = isFirstSpot ?
                HDF.PInvoke.H5F.create(szTempFile, HDF.PInvoke.H5F.ACC_TRUNC) :
                HDF.PInvoke.H5F.open(szTempFile, HDF.PInvoke.H5F.ACC_RDWR);

            try
            {
                // Pass isFirstSpot to indicate whether this is the first write
                WriteCompressedMatrix(fileId, "/temp_matrix", arrFullDoseMatrix, isFirstSpot);

                if (isFirstSpot)
                {
                    Log.Information($"Created new temporary file: {szTempFile}");
                }
                else
                {
                    Log.Information($"Appended to existing temporary file: {szTempFile}");
                }
            }
            finally
            {
                HDF.PInvoke.H5F.close(fileId);
            }
        }

        public static T[,] ConcatArrays2<T>(Array arr1, T[,] arr2) where T : struct
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

            for (int r = 0; r < iRowCnt; r++)
            {
                Buffer.BlockCopy(arr1, r * iRowSize1, arrResult, r * iRowSize, iRowSize1);
                Buffer.BlockCopy(arr2, r * iRowSize2, arrResult, r * iRowSize + iRowSize1, iRowSize2);
            }
            return arrResult;
        }

        public static void VerifyCompression(string filePath)
        {
            long fileId = -1;
            try
            {
                fileId = HDF.PInvoke.H5F.open(filePath, HDF.PInvoke.H5F.ACC_RDONLY);
                if (fileId < 0) throw new Exception("Failed to open file");

                // Get file size
                long fileSize = new FileInfo(filePath).Length;
                Log.Information($"HDF5 file size: {fileSize:N0} bytes");

                // Check dataset properties
                void CheckDataset(string datasetPath)
                {
                    long datasetId = HDF.PInvoke.H5D.open(fileId, datasetPath);
                    if (datasetId < 0) return;

                    try
                    {
                        long dcpl_id = HDF.PInvoke.H5D.get_create_plist(datasetId);
                        int nfilters = HDF.PInvoke.H5P.get_nfilters(dcpl_id);

                        ulong[] dims = new ulong[2];
                        long dataspaceId = HDF.PInvoke.H5D.get_space(datasetId);
                        HDF.PInvoke.H5S.get_simple_extent_dims(dataspaceId, dims, null);

                        long storage_size = (long)HDF.PInvoke.H5D.get_storage_size(datasetId);
                        double compression_ratio = (dims[0] * dims[1] * sizeof(double)) / (double)storage_size;

                        Log.Information($"Dataset: {datasetPath}");
                        Log.Information($"Dimensions: {dims[0]} x {dims[1]}");
                        Log.Information($"Storage size: {storage_size:N0} bytes");
                        Log.Information($"Compression ratio: {compression_ratio:F2}:1");
                        Log.Information($"Number of filters: {nfilters}");

                        HDF.PInvoke.H5P.close(dcpl_id);
                        HDF.PInvoke.H5S.close(dataspaceId);
                    }
                    finally
                    {
                        HDF.PInvoke.H5D.close(datasetId);
                    }
                }

                CheckDataset("/inf_matrix_full");
                CheckDataset("/inf_matrix_sparse");
            }
            finally
            {
                if (fileId >= 0)
                    HDF.PInvoke.H5F.close(fileId);
            }
        }

        // Try to add compression here

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
                using (Stream stream = System.IO.File.Open(szTempFile, FileMode.Open))
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
