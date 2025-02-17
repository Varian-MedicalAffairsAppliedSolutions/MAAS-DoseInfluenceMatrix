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
    public static class Helpers
    {
        // Spot weight is set to 100 so that the dose penumbra is better captured in case the "dose cut-off" setting of dose calculation is high.
        // The resulting dose values are divided by 100 before saving the results.
        public const float spotWeight = 100f;

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

        //public static void GetPatientInfoFromUser(ref string patientId, ref string courseId, ref string planId)
        //{
        //    Log.Information("Enter PatientId:");
        //    patientId = Console.ReadLine();
        //    Log.Information("Enter CourseId:");
        //    courseId = Console.ReadLine();
        //    Log.Information("Enter PlanId:");
        //    planId = Console.ReadLine();
        //}

        public static IonPlanSetup GetIonPlan(Application app, string patientId, string courseId, string planId)
        {
            //Log.Information($"Opening Patient \"{patientId}\"");
            Patient patient = app.OpenPatientById(patientId);
            if (patient is null)
            {
                throw new ApplicationException($"Could not find Patient with ID \"{patientId}\"");
            }
            patient.BeginModifications();

            //Log.Information($"Opening Plan \"{courseId} / {planId}\"");
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
            //Log.Information($"{planId} found.");
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
                            double pointDose = (doseBuffer[i, j] * dScale + dIntercept) / spotWeight;
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
                            double pointDose = (doseBuffer[i, j] * dScale + dIntercept) / spotWeight;
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

                        double pointDose = (doseBuffer[i, j] * dScale + dIntercept) / spotWeight;
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
            }
            return arrResult;
        }

        public static void CreateDataSet<T>(long fileId, string szDataSetName, Array arrDataSet) where T : struct
        {
            //CreateInitialDataset(fileId, szDataSetName, arrDataSet);
            //return;

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
            //bool fullMatrixExists = HDF.PInvoke.H5L.exists(fileId, szDataSetName) > 0;
            //if (!fullMatrixExists)
            //    CreateInitialDataset(fileId, szDataSetName, arrDataSet);
            //else
            //    AppendToDataset(fileId, szDataSetName, arrDataSet);
            //return;

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

            for (int i = iLen - 1; i >= 0; i--)
                Hdf5.CloseGroup(lstGroups[i]);
        }

        public static void AppendMatrixToTempFile<T>(string szTempFile, T[,] arrFullDoseMatrix) where T : struct
        {
            using (Stream stream = System.IO.File.Open(szTempFile, FileMode.Append))
            {
                BinaryFormatter bformatter = new BinaryFormatter();
                bformatter.Serialize(stream, arrFullDoseMatrix);
            }
        }

        public static void WriteInfMatrixHDF5(bool bExportFullInfMatrix, float[,] arrFullDoseMatrix, DoseData doseData, bool bAddLastEntry, int iMaxPointCnt, int iSpotIdx, string szPath)
        {
            long fileId = -1;
            try
            {
                //Log.Information($"Starting to write matrix to {szPath}");

                // First, handle file creation/opening
                if (!System.IO.File.Exists(szPath))
                {
                    fileId = HDF.PInvoke.H5F.create(szPath, HDF.PInvoke.H5F.ACC_TRUNC);
                    //Log.Information("Created new HDF5 file");
                }
                else
                {
                    fileId = HDF.PInvoke.H5F.open(szPath, HDF.PInvoke.H5F.ACC_RDWR);
                    //Log.Information("Opened existing HDF5 file");
                }

                if (fileId < 0)
                    throw new Exception("Failed to create/open HDF5 file");
                // Handle full matrix if requested
                if (bExportFullInfMatrix)
                {
                    bool fullMatrixExists = HDF.PInvoke.H5L.exists(fileId, "/inf_matrix_full") > 0;
                    //Log.Information($"Full matrix dataset {(fullMatrixExists ? "exists" : "does not exist")}");

                    if (!fullMatrixExists)
                    {
                        HDFCompression_Helpers.CreateInitialDataset(fileId, "/inf_matrix_full", arrFullDoseMatrix);
                    }
                    else
                    {
                        HDFCompression_Helpers.AppendToDataset(fileId, "/inf_matrix_full", arrFullDoseMatrix);
                    }
                }

                // write sparse inf matrix
                if (doseData?.dosePoints != null)
                { 
                    List<DosePoint> lstDosePoints = doseData.dosePoints;
                    int iPtCnt = lstDosePoints.Count;
                    double[,] arrSparse = new double[(bAddLastEntry ? iPtCnt + 1 : iPtCnt), 3];
                    for (int i = 0; i < iPtCnt; i++)
                    {
                        DosePoint dp = lstDosePoints[i];
                        arrSparse[i, 0] = dp.iPtIndex;
                        arrSparse[i, 1] = iSpotIdx;
                        arrSparse[i, 2] = dp.doseValue;
                    }
                    if (bAddLastEntry)
                    {
                        arrSparse[iPtCnt, 0] = iMaxPointCnt - 1;
                        arrSparse[iPtCnt, 1] = iSpotIdx;
                        arrSparse[iPtCnt, 2] = 0;
                    }

                    // Check if sparse matrix dataset exists
                    bool sparseMatrixExists = HDF.PInvoke.H5L.exists(fileId, "/inf_matrix_sparse") > 0;
                    //Log.Information($"Sparse matrix dataset {(sparseMatrixExists ? "exists" : "does not exist")}");

                    if (!sparseMatrixExists)
                    {
                        HDFCompression_Helpers.CreateInitialDataset(fileId, "/inf_matrix_sparse", arrSparse);
                    }
                    else
                    {
                        HDFCompression_Helpers.AppendToDataset(fileId, "/inf_matrix_sparse", arrSparse);
                    }
                }
            }
            catch (Exception ex)
            {
                //Log.Error($"Error in WriteInfMatrixHDF5: {ex.Message}");
                //Log.Error($"Stack trace: {ex.StackTrace}");
                throw ex;
            }
            finally
            {
                if (fileId >= 0)
                {
                    HDF.PInvoke.H5F.close(fileId);
                    //Log.Information("Successfully closed HDF5 file");
                }            
            }
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
    public abstract class DisplayProgress
    {
        public abstract void Message(string szMsg);
    }
}
