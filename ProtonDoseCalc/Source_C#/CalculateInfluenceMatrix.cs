using System;
using System.Linq;
using VMS.TPS.Common.Model.API;
using Serilog;
using System.Threading;
using System.Collections.Generic;

using System.Windows.Media.Media3D;
using VMS.TPS.Common.Model.Types;
using System.Security.Cryptography.X509Certificates;
using PureHDF;
using System.IO;
using System.Windows;
using HDF5CSharp;
using System.Windows.Shapes;
using HDF5DotNet;
using HDF.PInvoke;
using System.Security.Cryptography;
using System.Runtime.Serialization.Formatters.Binary;
using System.Windows.Media;
using System.Numerics;
using System.Windows.Controls;

[assembly: ESAPIScript(IsWriteable = true)]

namespace CalculateInfluenceMatrix
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

    public class CalculateInfluenceMatrix
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
            double dInfCutoffValue = System.Convert.ToDouble(System.Configuration.ConfigurationManager.AppSettings["InfCutoffValue"]);
            bool bExportFullInfMatrix = System.Configuration.ConfigurationManager.AppSettings["ExportFullInfMatrix"] == "1";
            string szOutputRootFolder = System.Configuration.ConfigurationManager.AppSettings["OutputRootFolder"];

            Log.Information($"Opening Patient \"{patientId}\"");
            Patient hPatient = app.OpenPatientById(patientId);
            if (hPatient is null)
            {
                throw new ApplicationException($"Could not find Patient with ID \"{patientId}\"");
            }
            Log.Information($"Opening Plan \"{courseId} / {planId}\"");
            Course course = hPatient.Courses.SingleOrDefault(x => x.Id.ToLower() == courseId.ToLower());
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
            string resultsDirPath = szOutputRootFolder + $"\\{hPatient.LastName}${hPatient.Id}";

            MyDisplayProgress hProgress = new MyDisplayProgress();
            VMS.TPS.Script.Calculate(hPatient, course, plan, bExportFullInfMatrix, dInfCutoffValue, resultsDirPath, hProgress);
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
    }
}
