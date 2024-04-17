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

        //public static DoseData GetNonZeroDosePoints(IonPlanSetup plan)
        //{
        //    if (plan.Dose is null)
        //    {
        //        throw new ApplicationException("Dose does not exist.");
        //    }

        //    List<DosePoint> doseFromSpot = new List<DosePoint>();
        //    for (int sliceIndex = 0; sliceIndex < plan.Dose.ZSize; sliceIndex++)
        //    {
        //        int[,] doseBuffer = new int[plan.Dose.XSize, plan.Dose.YSize];
        //        plan.Dose.GetVoxels(sliceIndex, doseBuffer);
        //        for (int i = 0; i < plan.Dose.XSize; i++)
        //        {
        //            for (int j = 0; j < plan.Dose.YSize; j++)
        //            {
        //                if (doseBuffer[i, j] > 0)
        //                {
        //                    double pointDose = plan.Dose.VoxelToDoseValue(doseBuffer[i, j]).Dose / CalculateInfluenceMatrix.spotWeight;
        //                    doseFromSpot.Add(new DosePoint(i, j, sliceIndex, pointDose));
        //                }
        //            }
        //        }
        //    }
        //    return new DoseData(doseFromSpot);
        //}
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
        public static DoseData GetNonZeroDosePoints(BeamDose hDose, ref double[,] arrFullDoseMatrix)
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
            int iDoseMatrixIdx = 0;
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
                        arrFullDoseMatrix[0,iDoseMatrixIdx++] = pointDose;
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

        public static void WriteResults_CVS(BeamMetaData hBeamData, DoseData doseData, string szPath)
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

        public static void WriteResults_HDF5(BeamMetaData hBeamData, double[,] arrFullDoseMatrix, DoseData doseData, int iLayerIdx, int iSpotIdx, string szPath)
        {
            string szMetaDataFile = Path.Combine(szPath, "Beam_" + hBeamData.ID + "_MetaData.json");
            WriteBeamMetaData(hBeamData, szMetaDataFile);

            string szDataFile = Path.Combine(szPath, "Beam_" + hBeamData.ID + "_Data.h5");
            WriteInfMatrixHDF5(hBeamData, arrFullDoseMatrix, doseData, iLayerIdx, iSpotIdx, szDataFile);
        }

        public static BeamMetaData PopulateBeamData(PlanSetup hPlan, Beam hBeam)
        {
            BeamMetaData hBeamData = new BeamMetaData();
            hBeamData.ID = hBeam.Id;
            hBeamData.iso_center = new IsocenterPosition(hBeam.IsocenterPosition.x, hBeam.IsocenterPosition.y, hBeam.IsocenterPosition.z);
            hBeamData.jaw_position = new JawPosition(hBeam.ControlPoints[0].JawPositions);
            hBeamData.doseMatrixSizeX = hBeam.Dose.XSize;
            hBeamData.doseMatrixSizeY = hBeam.Dose.YSize;
            hBeamData.doseMatrixSizeZ = hBeam.Dose.ZSize;
            return hBeamData;
        }
        private static double[,] createDataset(int offset = 0)
        {
            var dset = new double[10, 5];
            for (var i = 0; i < 10; i++)
                for (var j = 0; j < 5; j++)
                {
                    double x = i + j * 5 + offset;
                    dset[i, j] = (j == 0) ? x : x / 10;
                }
            return dset;
        }
        static void AppendOrCreateDataSet(long lGroupID, string szDataSetName, double[,] arrDataSet)
        {
            bool bAppend = Hdf5Utils.ItemExists(lGroupID, szDataSetName, Hdf5ElementType.Dataset);
            using (ChunkedDataset<double> chunkedDSet = new ChunkedDataset<double>(szDataSetName, lGroupID))
            {
                if (bAppend)
                {
                    double[,] dsets = Hdf5.ReadDataset<double>(lGroupID, szDataSetName).result as double[,];
                    chunkedDSet.AppendOrCreateDataset(dsets);
                    chunkedDSet.AppendDataset(arrDataSet);
                }
                else
                    chunkedDSet.AppendOrCreateDataset(arrDataSet);
            }
        }
        static void WriteInfMatrixHDF5(BeamMetaData hBeamData, double[,] arrFullDoseMatrix, DoseData doseData, int iLayerIdx, int iSpotIdx, string szPath) 
        {
            long fileId;
            bool bAppend = System.IO.File.Exists(szPath);
            if (bAppend)
                fileId = Hdf5.OpenFile(szPath);
            else
                fileId = Hdf5.CreateFile(szPath);

            // write full inf matrix
            AppendOrCreateDataSet(fileId, "/inf_matrix_full", arrFullDoseMatrix);

            // write sparse inf matrix
            List<DosePoint> lstDosePoints = doseData.dosePoints;
            int iPtCnt = lstDosePoints.Count;
            double[,] arrSparse = new double[iPtCnt, 4];
            int iSliceSize = hBeamData.doseMatrixSizeX * hBeamData.doseMatrixSizeY;
            for (int i = 0; i < iPtCnt; i++)
            {
                DosePoint dp = lstDosePoints[i];
                arrSparse[i, 0] = iLayerIdx;
                arrSparse[i, 1] = iSpotIdx;
                arrSparse[i, 2] = dp.sliceIndex * iSliceSize + dp.indexY * hBeamData.doseMatrixSizeX + dp.indexX;
                arrSparse[i, 3] = dp.doseValue;
            }
            AppendOrCreateDataSet(fileId, "/inf_matrix_sparse", arrSparse);

            Hdf5.CloseFile(fileId);
        }
        static void WriteBeamMetaData(BeamMetaData hBeamData, string szPath)
        {
            WriteJasonFile(hBeamData, szPath);
        }
        static void WriteJasonFile(Object hObj, string szPath)
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
