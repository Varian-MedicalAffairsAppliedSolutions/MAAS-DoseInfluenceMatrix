# MAAS-DoseInfluenceMatrix
This repository contain scripts to perform photon and proton dose calculation for the users having access to Eclipse TPS and save the data in the format compatible with [PortPy](https://github.com/PortPy-Project/PortPy). This data consist of all the necessary data for treatment plan optimization (e.g., beamlets, voxels, dose influence matrix). For more info about data, see [Data](https://github.com/PortPy-Project/PortPy?tab=readme-ov-file#data-).

You can run the photon/proton dose calculation using one of the following methods. Below is an example for doing dose calculation for photon. Similar steps can be followed for proton version by navigating to [ProtonDoseCalc](https://github.com/Varian-MedicalAffairsAppliedSolutions/MAAS-DoseInfluenceMatrix/tree/main/ProtonDoseCalc) repository 

#### **1. Using the Plugin (GUI Method)**
For users unfamiliar with command-line scripting, the plugin provides a **graphical interface** for dose calculation.

##### **Steps:**
1. Navigate to the [Plugin Directory](https://github.com/Varian-MedicalAffairsAppliedSolutions/MAAS-DoseInfluenceMatrix/tree/main/PhotonDoseCalc/Plugin/bin/Release) in photon dose calculation module  
2. Modify the config file:  
   - Edit [PhotonInfluenceMatrixCalcPlugin.esapi.config](https://github.com/Varian-MedicalAffairsAppliedSolutions/MAAS-DoseInfluenceMatrix/blob/main/PhotonDoseCalc/Source_C%23/bin/release/PhotonInfluenceMatrixCalcPlugin.esapi.config).  
   - Set `OutputRootFolder` to your preferred output directory.  You can also modify `BeamletSizeX` and `BeamletSizeY` for choosing the beamlet size in X and Y direction. Please modify the `EclipseVolumeDoseCalcModel` based on your dose calculation version available at your institution.
3. In Eclipse:
   - Open your patient plan (e.g., **Plan1**).  
   - Navigate to **Tools → Scripts → Change Folder**.  
   - Change the path to downloaded [Plugin Directory](https://github.com/Varian-MedicalAffairsAppliedSolutions/MAAS-DoseInfluenceMatrix/tree/main/PhotonDoseCalc/Plugin/bin/Release) and click **Open**.  
   - Select **PhotonInfluenceMatrixCalcPlugin.esapi.dll** and click **Run**
   - (Optional) Add the plugin to **Favorites** for quicker access.  

**Output:** PortPy-compatible data will be saved in the `OutputRootFolder` specified in the config file.


#### **2. Using the Executable (Command-Line Method)**
For users comfortable with command-line execution.

##### **Steps:**
1. Navigate to the [Executable Directory](https://github.com/Varian-MedicalAffairsAppliedSolutions/MAAS-DoseInfluenceMatrix/tree/main/PhotonDoseCalc/Source_C%23/bin/release).  
2. Modify the config file:
   - Edit [PhotonInfluenceMatrixCalc.exe.config](https://github.com/Varian-MedicalAffairsAppliedSolutions/MAAS-DoseInfluenceMatrix/blob/main/PhotonDoseCalc/Source_C%23/bin/release/PhotonInfluenceMatrixCalc.exe.config).  
   - Set `OutputRootFolder` to your preferred output directory. You can also modify `BeamletSizeX` and `BeamletSizeY` for choosing the beamlet size in X and Y direction. Please modify the `EclipseVolumeDoseCalcModel` based on your dose calculation version available at your institution.
     
3. Run the following command in a terminal or command prompt:  
   ```bash
   PhotonInfluenceMatrixCalc.exe <patient_mrn> <course_name> <plan_name>


The logs from the calculation are created in the "logs" directory. A directory is created for the results of the plan that was used
