using System;
using System.Windows;
using System.Reflection;
using System.Runtime.CompilerServices;
using VMS.TPS.Common.Model.API;

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
            window.Loaded += MainWindow_Loaded;

            if (context.PlanSetup == null || context.Image == null || context.Course == null || context.Patient == null)
            {
                System.Windows.MessageBox.Show("Please open a plan first.");
                m_bCloseMainWindowOnLoaded = true;
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

