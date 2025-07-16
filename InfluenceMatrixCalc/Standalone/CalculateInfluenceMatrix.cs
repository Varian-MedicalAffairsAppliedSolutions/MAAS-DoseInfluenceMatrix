using System;
using System.Linq;
using VMS.TPS.Common.Model.API;
using Serilog;
using System.IO;
using System.Reflection;
using System.Globalization;
using System.Windows;
using System.Windows.Media.Imaging;
using MAAS.Common.EulaVerification;

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
        // Define the project information for EULA verification
        private const string PROJECT_NAME = "DoseInfluenceMatrix";
        private const string PROJECT_VERSION = "1.0.0";
        private const string LICENSE_URL = "https://varian-medicalaffairsappliedsolutions.github.io/MAAS-DoseInfluenceMatrix/";
        private const string GITHUB_URL = "https://github.com/Varian-MedicalAffairsAppliedSolutions/MAAS-DoseInfluenceMatrix";

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                StartLogging();

                // Perform license verification
                if (!PerformLicenseVerification())
                {
                    Log.Warning("License verification failed or was declined.");
                    return;
                }

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

        static bool PerformLicenseVerification()
        {
            try
            {
                // Set up the EulaConfig directory
                string exePath = Assembly.GetExecutingAssembly().Location;
                string appDirectory = Path.GetDirectoryName(exePath);
                EulaConfig.ConfigDirectory = appDirectory;

                // Check for bypass files
                bool skipAgree = File.Exists(Path.Combine(appDirectory, "NoAgree.txt"));

                // Initialize EULA verification
                var eulaVerifier = new EulaVerifier(PROJECT_NAME, PROJECT_VERSION, LICENSE_URL);
                var eulaConfig = EulaConfig.Load(PROJECT_NAME);

                if (eulaConfig.Settings == null)
                {
                    eulaConfig.Settings = new ApplicationSettings();
                }

                bool eulaRequired = !skipAgree &&
                                    !eulaVerifier.IsEulaAccepted() &&
                                    !eulaConfig.Settings.EULAAgreed;

                if (eulaRequired)
                {
                    BitmapImage qrCode = null;
                    try
                    {
                        // For console applications, load QR code from file system since pack URI doesn't work
                        string qrCodePath = Path.Combine(appDirectory, "Resources", "qrcode.bmp");
                        if (File.Exists(qrCodePath))
                        {
                            qrCode = new BitmapImage();
                            qrCode.BeginInit();
                            qrCode.UriSource = new Uri(qrCodePath, UriKind.Absolute);
                            qrCode.EndInit();
                        }
                        else
                        {
                            Log.Debug($"QR code file not found at: {qrCodePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"QR code load failed: {ex.Message}");
                    }

                    if (eulaVerifier.ShowEulaDialog(qrCode))
                    {
                        eulaConfig.Settings.EULAAgreed = true;
                        eulaConfig.Settings.Validated = false; // Should only be set manually by editing code
                        eulaConfig.Save();
                    }
                    else
                    {
                        Log.Error("You must accept the license to use this application.");
                        return false;
                    }
                }

                // Check expiration
                var asmCa = typeof(CalculateInfluenceMatrix).Assembly.CustomAttributes
                    .FirstOrDefault(ca => ca.AttributeType.Name == "AssemblyExpirationDateAttribute");

                var bNoExpire = File.Exists(Path.Combine(appDirectory, "NOEXPIRE"));

                if (asmCa != null &&
                    DateTime.TryParse(asmCa.ConstructorArguments.FirstOrDefault().Value as string,
                        new CultureInfo("en-US"), DateTimeStyles.None, out DateTime endDate))
                {
                    if (DateTime.Now > endDate && !bNoExpire)
                    {
                        Log.Error("Application expiration date has passed.");
                        return false;
                    }

                    // Show expiration notice based on validation status
                    if (!skipAgree)
                    {
                        string msg;

                        if (!eulaConfig.Settings.Validated)
                        {
                            // First-time message
                            msg = $"The current DoseInfluenceMatrix application is provided AS IS as a non-clinical, research only tool in evaluation only. The current " +
                            $"application will only be available until {endDate.Date} after which the application will be unavailable. " +
                            "By Clicking 'Yes' you agree that this application will be evaluated and not utilized in providing planning decision support\n\n" +
                            $"Newer builds with future expiration dates can be found here: {GITHUB_URL}\n\n" +
                            "See the FAQ for more information on how to remove this pop-up and expiration";
                        }
                        else
                        {
                            // Returning user message
                            msg = $"Application will only be available until {endDate.Date} after which the application will be unavailable. " +
                            "By Clicking 'Yes' you agree that this application will be evaluated and not utilized in providing planning decision support\n\n" +
                            $"Newer builds with future expiration dates can be found here: {GITHUB_URL}\n\n" +
                            "See the FAQ for more information on how to remove this pop-up and expiration";
                        }

                        if (MessageBox.Show(msg, "Agreement", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                        {
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"License verification error: {ex.Message}");
                return false;
            }
        }



        static void Execute(VMS.TPS.Common.Model.API.Application app, string patientId, string courseId, string planId)
        {
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
            PlanSetup hPlan = hCourse.PlanSetups.SingleOrDefault(x => x.Id.ToLower() == planId.ToLower());
            if (hPlan is null)
            {
                throw new ApplicationException($"Could not find Plan with ID \"{planId}\"");
            }
            Log.Information($"{planId} found.");

            MyDisplayProgress hProgress = new MyDisplayProgress();
            bool bPhotonPlan = (hPlan as ExternalPlanSetup) != null;
            if (bPhotonPlan)
            {
                string szOutputRootFolder = System.Configuration.ConfigurationManager.AppSettings["Photon_OutputRootFolder"];
                double dInfCutoffValue = System.Convert.ToDouble(System.Configuration.ConfigurationManager.AppSettings["Photon_InfCutoffValue"]);
                bool bExportFullInfMatrix = System.Configuration.ConfigurationManager.AppSettings["Photon_ExportFullInfMatrix"] == "1";
                int iMaxDoseCalcRetry = System.Convert.ToInt32(System.Configuration.ConfigurationManager.AppSettings["Photon_MaxDoseCalcRetry"]);
                float beamletSizeX = System.Convert.ToSingle(System.Configuration.ConfigurationManager.AppSettings["Photon_BeamletSizeX"]);
                float beamletSizeY = System.Convert.ToSingle(System.Configuration.ConfigurationManager.AppSettings["Photon_BeamletSizeY"]);
                int iNumBeamletsToBeCalcAtATime = System.Convert.ToInt32(System.Configuration.ConfigurationManager.AppSettings["Photon_NumBeamletsToBeCalcAtATime"]);
                string szEclipseVolumeDoseCalcModel = System.Configuration.ConfigurationManager.AppSettings["Photon_EclipseVolumeDoseCalcModel"];
                string szCalculationGridSizeInCM = System.Configuration.ConfigurationManager.AppSettings["Photon_CalculationGridSizeInCM"];
                float fDoseScalingFactor = System.Convert.ToSingle(System.Configuration.ConfigurationManager.AppSettings["Photon_DoseScalingFactor"]);

                hProgress.Message("Influence Matrix Calculation Parameters:");
                hProgress.Message($"OutputRootFolder: {szOutputRootFolder}");
                hProgress.Message($"InfCutoffValue: {dInfCutoffValue}");
                hProgress.Message($"ExportFullInfMatrix: {bExportFullInfMatrix}");
                hProgress.Message($"MaxDoseCalcRetry: {iMaxDoseCalcRetry}");
                hProgress.Message($"BeamletSizeX: {beamletSizeX}");
                hProgress.Message($"BeamletSizeY: {beamletSizeY}");
                hProgress.Message($"NumBeamletsToBeCalcAtATime: {iNumBeamletsToBeCalcAtATime}");
                hProgress.Message($"EclipseVolumeDoseCalcModel: {szEclipseVolumeDoseCalcModel}");
                hProgress.Message($"CalculationGridSizeInCM: {szCalculationGridSizeInCM}");
                hProgress.Message($"DoseScalingFactor: {fDoseScalingFactor}");

                PhotonCalculateInfluenceMatrix.PhotonInfluenceMatrixCalc.Calculate(hPatient, hCourse, hPlan as ExternalPlanSetup, dInfCutoffValue, bExportFullInfMatrix, iMaxDoseCalcRetry, beamletSizeX, beamletSizeY,
                    iNumBeamletsToBeCalcAtATime, szEclipseVolumeDoseCalcModel, szCalculationGridSizeInCM, fDoseScalingFactor, szOutputRootFolder, hProgress);
            }
            else
            {
                double dInfCutoffValue = System.Convert.ToDouble(System.Configuration.ConfigurationManager.AppSettings["Proton_InfCutoffValue"]);
                bool bExportFullInfMatrix = System.Configuration.ConfigurationManager.AppSettings["Proton_ExportFullInfMatrix"] == "1";
                string szOutputRootFolder = System.Configuration.ConfigurationManager.AppSettings["Proton_OutputRootFolder"];
                string resultsDirPath = szOutputRootFolder + $"\\{hPatient.LastName}${hPatient.Id}";

                hProgress.Message("Influence Matrix Calculation Parameters:");
                hProgress.Message($"OutputRootFolder: {szOutputRootFolder}");
                hProgress.Message($"InfCutoffValue: {dInfCutoffValue}");
                hProgress.Message($"ExportFullInfMatrix: {bExportFullInfMatrix}");
                ProtonCalculateInfluenceMatrix.ProtonInfluenceMatrixCalc.Calculate(hPatient, hCourse, hPlan as IonPlanSetup, bExportFullInfMatrix, dInfCutoffValue, resultsDirPath, hProgress);
            }
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

