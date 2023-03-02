using System;
using System.Linq;
using VMS.TPS.Common.Model.API;
using Serilog;

[assembly: ESAPIScript(IsWriteable = true)]

namespace CalculateInfluenceMatrix
{
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
      if (plan.IonBeams.Count() != 1)
      {
        Log.Error("Influence matrix calculation is only supported for plans with exactly one field.");
        return;
      }

      if (plan.IonBeams.Any(beam => beam.BeamLineStatus != VMS.TPS.Common.Model.Types.ProtonBeamLineStatus.Valid))
      {
        Log.Error("Beamline is not valid.");
        return;
      }

      Helpers.SetAllSpotsToZero(plan);

      string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
      string resultsDirPath = System.IO.Path.GetDirectoryName(exePath) + "\\results";
      if (!System.IO.Directory.Exists(resultsDirPath))
      {
        System.IO.Directory.CreateDirectory(resultsDirPath);
      }
      string planResultsPath = resultsDirPath + $"\\{planId}";
      System.IO.Directory.CreateDirectory(planResultsPath);
      Log.Information($"Results will be written to: {planResultsPath}");

      for (int fieldIdx = 0; fieldIdx < plan.IonBeams.Count(); fieldIdx++)
      {
        IonBeam field = plan.IonBeams.ElementAt(fieldIdx);
        IonBeamParameters fieldParams = field.GetEditableParameters();
        IonControlPointPairCollection icpps = fieldParams.IonControlPointPairs;
        for (int layerIdx = 0; layerIdx < icpps.Count; layerIdx++)
        {
          IonSpotParametersCollection rawSpotList = icpps[layerIdx].RawSpotList;
          for (int spotIdx = 0; spotIdx < rawSpotList.Count; spotIdx++)
          {
            Log.Information($"Progress: Field {fieldIdx+1} / {plan.IonBeams.Count()}, Layer {layerIdx+1} / {icpps.Count}, Spot {spotIdx+1} / {rawSpotList.Count}.");
            rawSpotList[spotIdx].Weight = spotWeight;
            field.ApplyParameters(fieldParams);

            // When the raw spot list is modified above or in Helpers.SetAllSpotsToZero, the final spot list is cleared.
            // In this case, CalculateDoseWithoutPostProcessing calculates the dose using the raw spot list.
            CalculationResult calcRes = plan.CalculateDoseWithoutPostProcessing();
            if (!calcRes.Success)
            {
              throw new ApplicationException("Dose Calculation Failed");
            }

            DoseData doseData = Helpers.GetNonZeroDosePoints(plan);
            string filename = string.Format("field{0}-layer{1}-spot{2}-results.csv", fieldIdx, layerIdx, spotIdx);
            string filepath = string.Format(planResultsPath + "\\{0}", filename);
            Helpers.WriteResults(doseData, filepath);

            rawSpotList[spotIdx].Weight = 0.0f;
            field.ApplyParameters(fieldParams);
          }
        }
      }
      
      Log.Information("Influence matrix calculation finished.");
    }
  }
}
