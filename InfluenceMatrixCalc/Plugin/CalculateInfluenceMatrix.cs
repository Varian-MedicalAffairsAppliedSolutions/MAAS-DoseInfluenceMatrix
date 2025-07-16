using System;
using System.Windows;
using System.Reflection;
using System.Runtime.CompilerServices;
using VMS.TPS.Common.Model.API;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Windows.Media.Imaging;
using MAAS.Common.EulaVerification;

using CalculateInfluenceMatrix;

// TODO: Uncomment the following line if the script requires write access.
[assembly: ESAPIScript(IsWriteable = true)]

namespace VMS.TPS
{
    class MyDisplayProgress : DisplayProgress
    {
        ctrlMain m_ctrlMain;

        public MyDisplayProgress(ctrlMain ctrl)
        {
            m_ctrlMain = ctrl;
        }

        public override void Message(string szMsg)
        {
            m_ctrlMain.AddMessage(szMsg);

            // Check for cancellation on each message
            if (CalculateInfluenceMatrix.ctrlMain.CancellationRequested)
            {
                m_ctrlMain.AddMessage("Cancellation requested. Stopping calculation...");
                throw new OperationCanceledException("Operation was cancelled by user");
            }
        }
    }

    public class Script
    {
        // Define the project information for EULA verification
        private const string PROJECT_NAME = "DoseInfluenceMatrix";
        private const string PROJECT_VERSION = "1.0.0";
        private const string LICENSE_URL = "https://varian-medicalaffairsappliedsolutions.github.io/MAAS-DoseInfluenceMatrix/";
        private const string GITHUB_URL = "https://github.com/Varian-MedicalAffairsAppliedSolutions/MAAS-DoseInfluenceMatrix";

        bool m_bCloseMainWindowOnLoaded;
        ctrlMain m_ctrlMain;
        public bool CancelRequested { get; set; } = false;

        Patient m_hPatient;
        Course m_hCourse;
        PlanSetup m_hPlanSetup;
        bool m_bPhotonPlan;

        public Script()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context, System.Windows.Window window)
        {
            try
            {
                // Set up the EulaConfig directory
                string scriptPath = Assembly.GetExecutingAssembly().Location;
                string scriptDirectory = Path.GetDirectoryName(scriptPath);
                EulaConfig.ConfigDirectory = scriptDirectory;

                // EULA verification
                var eulaVerifier = new EulaVerifier(PROJECT_NAME, PROJECT_VERSION, LICENSE_URL);
                var eulaConfig = EulaConfig.Load(PROJECT_NAME);
                if (eulaConfig.Settings == null)
                {
                    eulaConfig.Settings = new ApplicationSettings();
                }

                if (!eulaVerifier.IsEulaAccepted())
                {
                    MessageBox.Show(
                        $"This version of {PROJECT_NAME} (v{PROJECT_VERSION}) requires license acceptance before first use.\n\n" +
                        "You will be prompted to provide an access code. Please follow the instructions to obtain your code.",
                        "License Acceptance Required",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    BitmapImage qrCode = null;
                    try
                    {
                        string assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
                        qrCode = new BitmapImage(new Uri($"pack://application:,,,/{assemblyName};component/Resources/qrcode.bmp"));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading QR code: {ex.Message}");
                    }

                    if (!eulaVerifier.ShowEulaDialog(qrCode))
                    {
                        MessageBox.Show(
                            "License acceptance is required to use this application.\n\n" +
                            "The application will now close.",
                            "License Not Accepted",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }
                }

                // Check expiration date
                var asmCa = typeof(Script).Assembly.CustomAttributes
                    .FirstOrDefault(ca => ca.AttributeType.Name == "AssemblyExpirationDateAttribute");

                var bNoExpire = File.Exists(Path.Combine(scriptDirectory, "NOEXPIRE"));

                if (asmCa != null &&
                    DateTime.TryParse(asmCa.ConstructorArguments.FirstOrDefault().Value as string,
                        new CultureInfo("en-US"), DateTimeStyles.None, out DateTime endDate))
                {
                    if (DateTime.Now > endDate && !bNoExpire)
                    {
                        MessageBox.Show($"Application has expired. Newer builds with future expiration dates can be found here: {GITHUB_URL}",
                                        "MAAS-DoseInfluenceMatrix",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Information);
                        return;
                    }

                    if (!bNoExpire)
                    {
                        string msg;
                        if (!eulaConfig.Settings.Validated)
                        {
                            // First-time message
                            msg = $"The current MAAS-DoseInfluenceMatrix application is provided AS IS as a non-clinical, research only tool in evaluation only. The current " +
                            $"application will only be available until {endDate.Date} after which the application will be unavailable. " +
                            $"By Clicking 'Yes' you agree that this application will be evaluated and not utilized in providing planning decision support\n\n" +
                            $"Newer builds with future expiration dates can be found here: {GITHUB_URL}\n\n" +
                            "See the FAQ for more information on how to remove this pop-up and expiration";
                        }
                        else
                        {
                            // Returning user message
                            msg = $"Application will only be available until {endDate.Date} after which the application will be unavailable. " +
                            "By Clicking 'Yes' you agree that this application will be evaluated and not utilized in providing planning decision support\n\n" +
                            $"Newer builds with future expiration dates can be found here: {GITHUB_URL} \n\n" +
                            "See the FAQ for more information on how to remove this pop-up and expiration";
                        }

                        if (MessageBox.Show(msg, "Agreement", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                        {
                            return;
                        }
                    }
                }

                window.Loaded += MainWindow_Loaded;

                if (context.PlanSetup == null || context.Image == null || context.Course == null || context.Patient == null)
                {
                    System.Windows.MessageBox.Show("Please open a plan first.");
                    m_bCloseMainWindowOnLoaded = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during license verification: {ex.Message}",
                               "License Verification Error",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
                return;
            }

            m_hPatient = context.Patient;
            m_hCourse = context.Course;
            m_bPhotonPlan = context.ExternalPlanSetup != null;
            m_hPlanSetup = context.PlanSetup;

            m_ctrlMain = new ctrlMain(this, window, (m_bPhotonPlan? "Photon Influence Matrix Calculator": "Proton Influence Matrix Calculator"));
            window.Title = "Calculate Influence Matrix";
            window.Content = m_ctrlMain;
            window.Height = 400;
            window.Width = 600;
        }

        void MainWindow_Loaded(object sender, EventArgs e)
        {
            if (m_bCloseMainWindowOnLoaded)
                (sender as Window).Close();
        }


        public void RunInfMatrixCalc()
        {
            try
            {
                System.Configuration.Configuration hConfig = System.Configuration.ConfigurationManager.OpenExeConfiguration(this.GetType().Assembly.Location);

                MyDisplayProgress hProgress = new MyDisplayProgress(m_ctrlMain);
                if (m_bPhotonPlan)
                {
                    string szOutputRootFolder = hConfig.AppSettings.Settings["Photon_OutputRootFolder"].Value;
                    double dInfCutoffValue = System.Convert.ToDouble(hConfig.AppSettings.Settings["Photon_InfCutoffValue"].Value);
                    bool bExportFullInfMatrix = hConfig.AppSettings.Settings["Photon_ExportFullInfMatrix"].Value == "1";
                    int iMaxDoseCalcRetry = System.Convert.ToInt32(hConfig.AppSettings.Settings["Photon_MaxDoseCalcRetry"].Value);
                    float beamletSizeX = System.Convert.ToSingle(hConfig.AppSettings.Settings["Photon_BeamletSizeX"].Value);
                    float beamletSizeY = System.Convert.ToSingle(hConfig.AppSettings.Settings["Photon_BeamletSizeY"].Value);
                    int iNumBeamletsToBeCalcAtATime = System.Convert.ToInt32(hConfig.AppSettings.Settings["Photon_NumBeamletsToBeCalcAtATime"].Value);
                    string szEclipseVolumeDoseCalcModel = hConfig.AppSettings.Settings["Photon_EclipseVolumeDoseCalcModel"].Value;
                    string szCalculationGridSizeInCM = hConfig.AppSettings.Settings["Photon_CalculationGridSizeInCM"].Value;
                    float fDoseScalingFactor = System.Convert.ToSingle(hConfig.AppSettings.Settings["Photon_DoseScalingFactor"].Value);

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
                    PhotonCalculateInfluenceMatrix.PhotonInfluenceMatrixCalc.Calculate(m_hPatient, m_hCourse, m_hPlanSetup as ExternalPlanSetup, dInfCutoffValue, bExportFullInfMatrix, iMaxDoseCalcRetry, beamletSizeX, beamletSizeY,
                        iNumBeamletsToBeCalcAtATime, szEclipseVolumeDoseCalcModel, szCalculationGridSizeInCM, fDoseScalingFactor, szOutputRootFolder, hProgress,
                        () => CalculateInfluenceMatrix.ctrlMain.CancellationRequested);
                }
                else
                {
                    double dInfCutoffValue = System.Convert.ToDouble(hConfig.AppSettings.Settings["Proton_InfCutoffValue"].Value);
                    bool bExportFullInfMatrix = hConfig.AppSettings.Settings["Proton_ExportFullInfMatrix"].Value == "1";
                    string szOutputRootFolder = hConfig.AppSettings.Settings["Proton_OutputRootFolder"].Value;
                    string resultsDirPath = szOutputRootFolder + $"\\{m_hPatient.LastName}${m_hPatient.Id}";

                    hProgress.Message("Influence Matrix Calculation Parameters:");
                    hProgress.Message($"OutputRootFolder: {szOutputRootFolder}");
                    hProgress.Message($"InfCutoffValue: {dInfCutoffValue}");
                    hProgress.Message($"ExportFullInfMatrix: {bExportFullInfMatrix}");
                    ProtonCalculateInfluenceMatrix.ProtonInfluenceMatrixCalc.Calculate(m_hPatient, m_hCourse, m_hPlanSetup as IonPlanSetup, bExportFullInfMatrix, dInfCutoffValue, szOutputRootFolder, hProgress, 
                        () => CalculateInfluenceMatrix.ctrlMain.CancellationRequested);
                }
            } //
            catch (OperationCanceledException)
            {
                if (m_ctrlMain != null)
                    m_ctrlMain.AddMessage("Calculation was cancelled.");
            }
        }
    }
}

