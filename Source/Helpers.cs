using System;
using System.Linq;
using System.Collections.Generic;
using VMS.TPS.Common.Model.API;
using Serilog;
using System.IO;
using System.Text;

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

    public static DoseData GetNonZeroDosePoints(IonPlanSetup plan)
    {
      if (plan.Dose is null)
      {
        throw new ApplicationException("Dose does not exist.");
      }

      List<DosePoint> doseFromSpot = new List<DosePoint>();
      for (int sliceIndex = 0; sliceIndex < plan.Dose.ZSize; sliceIndex++)
      {
        int[,] doseBuffer = new int[plan.Dose.XSize, plan.Dose.YSize];
        plan.Dose.GetVoxels(sliceIndex, doseBuffer);
        for (int i = 0; i < plan.Dose.XSize; i++)
        {
          for (int j = 0; j < plan.Dose.YSize; j++)
          {
            if (doseBuffer[i, j] > 0)
            {
              double pointDose = plan.Dose.VoxelToDoseValue(doseBuffer[i, j]).Dose / CalculateInfluenceMatrix.spotWeight;
              doseFromSpot.Add(new DosePoint(i, j, sliceIndex, pointDose));
            }
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

    public static void WriteResults(DoseData doseData, string path)
    {
      StringBuilder builder = new StringBuilder();
      string header = "sliceIndex,yIndex,xIndex,dose";
      builder.AppendLine(header);
      foreach (DosePoint dosePoint in doseData.dosePoints)
      {
        builder.AppendLine($"{dosePoint.sliceIndex},{dosePoint.indexY},{dosePoint.indexX},{dosePoint.doseValue}");
      }
      File.WriteAllText(path, builder.ToString());
    }
  }

}
