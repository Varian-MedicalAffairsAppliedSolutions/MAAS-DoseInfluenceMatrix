# MAAS-DoseInfluenceMatrix 
This repository contain scripts to perform photon and proton dose calculation for the users having access to Eclipse TPS and save the data in the format compatible with [PortPy](https://github.com/PortPy-Project/PortPy). This data consist of all the necessary data for treatment plan optimization (e.g., beamlets, voxels, dose influence matrix). 

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

## PortPy Data Format
PortPy uses a structured data format to store patient-specific treatment planning data needed for optimization. The format consists of two main components:

1. **Metadata (JSON format)**  
2. **Numerical Data (HDF5 format)**  

#### 1. Metadata (JSON)
The metadata file stores essential information about the beam configuration, structure sets, and dose voxels. It contains references to the numerical data stored in HDF5 files. Below is an example of the beams metadata:

**Beam_0_metadata.json**
```json
{
  "ID": 0,
  "gantry_angle": 0,
  "collimator_angle": 0,
  "couch_angle": 0,
  "iso_center": {
    "x_mm": 119.2041,
    "y_mm": 60.53891,
    "z_mm": -9.122542
  },
  "beamlets": {
    "id_File": "Beam_0_Data.h5/beamlets/id",
    "width_mm_File": "Beam_0_Data.h5/beamlets/width_mm",
    "height_mm_File": "Beam_0_Data.h5/beamlets/height_mm",
    "position_x_mm_File": "Beam_0_Data.h5/beamlets/position_x_mm",
    "position_y_mm_File": "Beam_0_Data.h5/beamlets/position_y_mm",
    "MLC_leaf_idx_File": "Beam_0_Data.h5/beamlets/MLC_leaf_idx"
  },
  "jaw_position": {
    "top_left_x_mm": -5,
    "top_left_y_mm": 40,
    "bottom_right_x_mm": 97.5,
    "bottom_right_y_mm": -60
  },
  "influenceMatrixSparse_File": "Beam_0_Data.h5/inf_matrix_sparse",
  "influenceMatrixFull_File": "Beam_0_Data.h5/inf_matrix_full",
  "MLC_leaves_pos_y_mm_File": "Beam_0_Data.h5/MLC_leaves_pos_y_mm"
}
```

#### 2. Numerical Data (HDF5)
The numerical data is stored in HDF5 format, organized in a hierarchical structure. The metadata files contain pointers to these HDF5 datasets. Below is an example for Beams Data in hdf5 format

**Beam_0_Data.h5**
```
Beam_0_Data.h5
│
├── beamlets/
│   ├── id               (1D array of beamlet IDs)
│   ├── width_mm         (1D array of beamlet widths in mm)
│   ├── height_mm        (1D array of beamlet heights in mm)
│   ├── position_x_mm    (1D array of x positions in mm)
│   ├── position_y_mm    (1D array of y positions in mm)
│   └── MLC_leaf_idx     (1D array of MLC leaf indices)
│
├── inf_matrix_sparse    (Sparse influence matrix)
├── inf_matrix_full      (Full influence matrix)
└── MLC_leaves_pos_y_mm (MLC leaves positions in mm in y direction)
```
#### Data Relationships
**Beam Metadata ↔ Beam HDF5 Data**

The JSON metadata contains file paths pointing to datasets in the HDF5 file. They end with '_File' in the end the pointer.
Example: "influenceMatrixFull_File": "Beam_0_Data.h5/inf_matrix_full"

**Structure Metadata ↔ Structure HDF5 Data**
Example: "structure_mask_3d_File": "StructureSet_Data.h5/PTV"


For more info about data, see [Data](https://github.com/PortPy-Project/PortPy?tab=readme-ov-file#data-).
