using System;
using System.Linq;
using VMS.TPS.Common.Model.API;
using Serilog;

[assembly: ESAPIScript(IsWriteable = true)]

namespace PhotonInfluenceMatrixCalc
{
    class MyDisplayProgress : DisplayProgress
    {
        public MyDisplayProgress()
        {
        }

        public override void Message(string szMsg)
        {
            Log.Information(szMsg);
        }
    }
	
    public class PhotonInfluenceMatrixCalc
    {
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                StartLogging();

                string patientId = "";
                string courseId = "";
                string planId = "";
                if (!ParseInputArgs(args, ref patientId, ref courseId, ref planId))
                {
                    GetPatientInfoFromUser(ref patientId, ref courseId, ref planId);
                }
                using (VMS.TPS.Common.Model.API.Application app = VMS.TPS.Common.Model.API.Application.CreateApplication())
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

        static void Execute(VMS.TPS.Common.Model.API.Application app, string patientId, string courseId, string planId)
        {
            string szOutputRootFolder = System.Configuration.ConfigurationManager.AppSettings["OutputRootFolder"];
            double dInfCutoffValue = System.Convert.ToDouble(System.Configuration.ConfigurationManager.AppSettings["InfCutoffValue"]);
            bool bExportFullInfMatrix = System.Configuration.ConfigurationManager.AppSettings["ExportFullInfMatrix"]=="1";
            int iMaxDoseCalcRetry = System.Convert.ToInt32(System.Configuration.ConfigurationManager.AppSettings["MaxDoseCalcRetry"]);
            float beamletSizeX = System.Convert.ToSingle(System.Configuration.ConfigurationManager.AppSettings["BeamletSizeX"]);
            float beamletSizeY = System.Convert.ToSingle(System.Configuration.ConfigurationManager.AppSettings["BeamletSizeY"]);
            int iNumBeamletsToBeCalcAtATime = System.Convert.ToInt32(System.Configuration.ConfigurationManager.AppSettings["NumBeamletsToBeCalcAtATime"]);
            string szEclipseVolumeDoseCalcModel = System.Configuration.ConfigurationManager.AppSettings["EclipseVolumeDoseCalcModel"];
            string szCalculationGridSizeInCM = System.Configuration.ConfigurationManager.AppSettings["CalculationGridSizeInCM"];
            float fDoseScalingFactor = System.Convert.ToSingle(System.Configuration.ConfigurationManager.AppSettings["DoseScalingFactor"]);

            //PlanSetup plan = Helpers.GetPlan(app, patientId, courseId, planId);
            Log.Information($"Opening Patient \"{patientId}\"");
            Patient hPatient = app.OpenPatientById(patientId);
            if (hPatient is null)
            {
                throw new ApplicationException($"Could not find Patient with ID \"{patientId}\"");
            }

            Log.Information($"Opening Plan \"{courseId} / {planId}\"");
            Course hCourse = hPatient.Courses.SingleOrDefault(x => x.Id.ToLower() == courseId.ToLower());
            if (hCourse is null)
            {
                throw new ApplicationException($"Could not find Course with ID \"{courseId}\"");
            }
            ExternalPlanSetup hPlan = (ExternalPlanSetup)hCourse.PlanSetups.SingleOrDefault(x => x.Id.ToLower() == planId.ToLower());
            if (hPlan is null)
            {
                throw new ApplicationException($"Could not find Plan with ID \"{planId}\"");
            }
            Log.Information($"{planId} found.");

            MyDisplayProgress hProgress = new MyDisplayProgress();
            VMS.TPS.Script.Calculate(hPatient, hCourse, hPlan, dInfCutoffValue, bExportFullInfMatrix, iMaxDoseCalcRetry, beamletSizeX, beamletSizeY,
                iNumBeamletsToBeCalcAtATime, szEclipseVolumeDoseCalcModel, szCalculationGridSizeInCM, fDoseScalingFactor, szOutputRootFolder, hProgress);
        }
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
            if (args.Length == 0)
                return false;
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
    }
}
