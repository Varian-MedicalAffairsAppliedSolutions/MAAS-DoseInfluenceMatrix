using System;
using System.Linq;
using System.Collections.Generic;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using HDF5CSharp;
using HDF5CSharp.DataTypes;
using System.Runtime.InteropServices;

using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using HDF5DotNet;
using PureHDF;
using System.Runtime.Serialization.Formatters.Binary;
using Newtonsoft.Json.Linq;

namespace PhotonInfluenceMatrixCalc
{
    static class Helpers
    {
        public static DoseData GetDosePoints(BeamDose hDose, double dWeight, double dCutoffValue, ref float[,] arrFullDoseMatrix)
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
            List<DosePoint> lstBeamletDose = new List<DosePoint>();
            double dSumCutOffValues = 0;
            int iCutOffValueCnt = 0;
            int iIdxOffset = 0, iPtIndex;
            for (int sliceIndex = 0; sliceIndex < iZSize; sliceIndex++)
            {
                hDose.GetVoxels(sliceIndex, doseBuffer);    // values are in
                iIdxOffset = sliceIndex * iYSize * iXSize;
                for (int j = 0; j < iYSize; j++)
                {
                    for (int i = 0; i < iXSize; i++)
                    {
                        iPtIndex = iIdxOffset + j * iXSize + i;

                        //double pointDose1 = hDose.VoxelToDoseValue(doseBuffer[i, j]).Dose;
                        double pointDose = doseBuffer[i, j] * dScale + dIntercept;
                        //if (Math.Abs(pointDose - pointDose1) > 0.0000001)
                        //    throw new ApplicationException("dose values don't match");

                        pointDose = pointDose / dWeight;

                        if (pointDose > dCutoffValue)
                        {
                            lstBeamletDose.Add(new DosePoint(iPtIndex, pointDose));
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
            return new DoseData(lstBeamletDose, dSumCutOffValues, iCutOffValueCnt);
        }

        public static void WriteBeamMetaData(Beam b, MyBeamParameters beamParams, double dInfMatrixCutoffValue, string szOutputFile)
        {
            ControlPoint firstCP = b.ControlPoints[0];

            string szBeamID = b.Id;
            string szFilename = $"Beam_{szBeamID}_Data.h5";
            Dictionary<string, object> dctBeamData = new Dictionary<string, object>
            {
                { "ID", szBeamID },
                { "gantry_angle", (float)firstCP.GantryAngle },
                { "couch_angle", (float)firstCP.PatientSupportAngle },
                { "collimator_angle" , (float)firstCP.CollimatorAngle },
                { "iso_center", new Dictionary<string, float> {
                        { "x_mm", (float)b.IsocenterPosition.x },
                        { "y_mm", (float)b.IsocenterPosition.y },
                        { "z_mm", (float)b.IsocenterPosition.z }
                   }
                },
                { "beamlets", new Dictionary<string, string> {
                        { "id_File" , $"{szFilename}/beamlets/id" },
                        { "width_mm_File" , $"{szFilename}/beamlets/width_mm" },
                        { "height_mm_File" , $"{szFilename}/beamlets/height_mm" },
                        { "position_x_mm_File" , $"{szFilename}/beamlets/position_x_mm" },
                        { "position_y_mm_File" , $"{szFilename}/beamlets/position_y_mm" },
                        { "MLC_leaf_idx_File" , $"{szFilename}/beamlets/MLC_leaf_idx" }
                    }
                },
                { "jaw_position" , new Dictionary<string, float>{ { "top_left_x_mm", (float)firstCP.JawPositions.X1 }, { "top_left_y_mm", (float)firstCP.JawPositions.Y1 }, { "bottom_right_x_mm", (float)firstCP.JawPositions.X2 }, {"bottom_right_y_mm", (float)firstCP.JawPositions.Y2 } } },
                { "BEV_structure_contour_points_File" , $"{szFilename}/BEV_structure_contour_points"},
                { "MLC_name" ,  b.MLC.Name},
                { "beam_modality" , b.Technique.Id},
                { "energy_MV" ,  b.EnergyModeDisplayName},
                { "SSD_mm" , b.SSD},
                { "SAD_mm" , b.TreatmentUnit.SourceAxisDistance},
                { "influenceMatrixSparse_File", $"{szFilename}/inf_matrix_sparse" },
                { "influenceMatrixSparse_tol", dInfMatrixCutoffValue },
                { "influenceMatrixFull_File", $"{szFilename}/inf_matrix_full" },
                { "MLC_leaves_pos_y_mm_File" ,  $"{szFilename}/MLC_leaves_pos_y_mm"},
                { "machine_name" , b.TreatmentUnit.Id}
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
            for (int r = 0; r < iRowCnt; r++)
            {
                //for (int c = 0; c < iColCnt1; c++)
                //    arrResult[r, c] = (T)arr1.GetValue(r, c);

                //for (int c = 0; c < iColCnt2; c++)
                //    arrResult[r, c+iColCnt1] = arr2[r, c];

                Buffer.BlockCopy(arr1, r * iRowSize1, arrResult, r * iRowSize, iRowSize1);
                Buffer.BlockCopy(arr2, r * iRowSize2, arrResult, r * iRowSize + iRowSize1, iRowSize2);
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
            using (Stream stream = File.Open(szTempFile, FileMode.Append))
            {
                BinaryFormatter bformatter = new BinaryFormatter();
                bformatter.Serialize(stream, arrFullDoseMatrix);
            }
        }

        public static void WriteClosedMLCsInfMatrixHDF5(float[,] arrFullDoseMatrix, string szPath)
        {
            long fileId;
            bool bAppend = System.IO.File.Exists(szPath);
            if (bAppend)
                fileId = Hdf5.OpenFile(szPath);
            else
                fileId = Hdf5.CreateFile(szPath);
            Helpers.CreateDataSet<float>(fileId, "/closed_MLCs_inf_matrix_full", arrFullDoseMatrix);

            Hdf5.CloseFile(fileId);
        }
        public static void WriteInfMatrixHDF5(bool bExportFullInfMatrix, float[,] arrFullDoseMatrix, DoseData doseData, bool bAddLastEntry, int iMaxPointCnt, int iBeamletIdx, string szPath)
        {
            long fileId;
            bool bAppend = System.IO.File.Exists(szPath);
            if (bAppend)
                fileId = Hdf5.OpenFile(szPath);
            else
                fileId = Hdf5.CreateFile(szPath);
            string szTempFile = szPath + ".tmp";
            if (bExportFullInfMatrix)
            {
                // write full inf matrix
                //Helpers.AddOrAppendDataSet2<double>(fileId, "/inf_matrix_full", arrFullDoseMatrix);
                Helpers.AppendMatrixToTempFile<float>(szTempFile, arrFullDoseMatrix);
            }

            // write sparse inf matrix
            List<DosePoint> lstDosePoints = doseData.dosePoints;
            int iPtCnt = lstDosePoints.Count;
            double[,] arrSparse = new double[(bAddLastEntry ? iPtCnt + 1 : iPtCnt), 3];
            for (int i = 0; i < iPtCnt; i++)
            {
                DosePoint dp = lstDosePoints[i];
                arrSparse[i, 0] = dp.iPtIndex;
                arrSparse[i, 1] = iBeamletIdx;
                arrSparse[i, 2] = dp.doseValue;
            }
            if (bAddLastEntry)
            {
                arrSparse[iPtCnt, 0] = iMaxPointCnt - 1;
                arrSparse[iPtCnt, 1] = iBeamletIdx;
                arrSparse[iPtCnt, 2] = 0;
            }
            Helpers.AddOrAppendDataSet<double>(fileId, "/inf_matrix_sparse", arrSparse);

            Hdf5.CloseFile(fileId);
        }
        public static void WriteBeamletInfoHDF5(MyBeamParameters beamParams, string szPath)
        {
            long fileId;
            bool bAppend = System.IO.File.Exists(szPath);
            if (bAppend)
                fileId = Hdf5.OpenFile(szPath);
            else
                fileId = Hdf5.CreateFile(szPath);

            int iBeamletCnt = beamParams.BeamletCount;
            int[] arrId = new int[iBeamletCnt];
            float[] arrXPos = new float[iBeamletCnt];
            float[] arrYPos = new float[iBeamletCnt];
            float[] arrXSize = new float[iBeamletCnt];
            float[] arrYSize = new float[iBeamletCnt];
            double[] arrSumOfCutoffValues = new double[iBeamletCnt];
            int[] arrNumCutoffValues = new int[iBeamletCnt];
            for ( int i=0; i<iBeamletCnt; i++)
            {
                Beamlet bl = beamParams.m_lstBeamlets[i];
                arrId[i] = bl.m_iIndex;
                arrXPos[i] = bl.m_fXStart + bl.m_fXSize/2.0f;
                arrYPos[i] = bl.m_fYStart + bl.m_fYSize / 2.0f;
                arrXSize[i] = bl.m_fXSize;
                arrYSize[i] = bl.m_fYSize;
                arrSumOfCutoffValues[i] = bl.m_dSumCutoffValues;
                arrNumCutoffValues[i] = bl.m_iNumCutoffValues;
            }
            Helpers.CreateDataSet<int>(fileId, "/beamlets/id", arrId);
            Helpers.CreateDataSet<float>(fileId, "/beamlets/position_x_mm", arrXPos);
            Helpers.CreateDataSet<float>(fileId, "/beamlets/width_mm", arrXSize);
            Helpers.CreateDataSet<float>(fileId, "/beamlets/position_y_mm", arrYPos);
            Helpers.CreateDataSet<float>(fileId, "/beamlets/height_mm", arrYSize);
            Helpers.CreateDataSet<double>(fileId, "/beamlets/sum_cutoff_value", arrSumOfCutoffValues);
            Helpers.CreateDataSet<int>(fileId, "/beamlets/cutoff_value_cnt", arrNumCutoffValues);

            // create fullmatrix dataset from temp file
            string szTempFile = szPath + ".tmp";
            if (System.IO.File.Exists(szTempFile))
            {
                using (Stream stream = File.Open(szTempFile, FileMode.Open))
                {
                    BinaryFormatter bformatter = new BinaryFormatter();
                    float[,] arrFullDoseMatrix = null;
                    int iCurrCol = 0;
                    try
                    {
                        while (true)
                        {
                            float[,] arr = (float[,])bformatter.Deserialize(stream);
                            int iPtCnt = arr.GetLength(0);
                            if (arrFullDoseMatrix == null)
                                arrFullDoseMatrix = new float[iPtCnt, iBeamletCnt];
                             
                            for(int r=0; r< iPtCnt; r++ )
                                arrFullDoseMatrix[r,iCurrCol] = arr[r,0];
                            iCurrCol++;
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
    public abstract class DisplayProgress
    {
        public abstract void Message(string szMsg);
    }

}
