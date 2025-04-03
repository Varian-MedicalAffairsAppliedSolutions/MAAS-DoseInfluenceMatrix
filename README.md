# MAAS-DoseInfluenceMatrix 

## Introduction 
This repository contains ESAPI scripts to extract all data required for performing treatment planning optimization outside the Eclipse environment. An Eclipse instance is required to extract the data. Once the data is obtained, a treatment planning optimization software, such as [PortPy](https://github.com/cqad/PortPy), can load the data and perform the planning optimization. This approach provides flexibility to researchers who want to develop novel treatment planning optimization techniques that are not readily available within ESAPI.

**Note:** Only the photon version is currently available. The proton version is a work in progress.  
The script loops through each field, subdivides the beam into small beamlets (or spots in proton mode), and calculates dose for each beamlet. Hence, the process can be computationally demanding and time-consuming.

## Data and data format
Each beam is divided into small 2D beamlets/spots, and the patient’s body is divided into small 3D voxels. Eclipse is used to calculate the dose contribution of each beamlet to every voxel, resulting in a **dose influence matrix** (also called a dose deposition matrix or dij matrix). Relevant beamlet and voxel information (e.g., size, coordinates) is stored, as well as CT data (e.g., voxel Hounsfield Units, coordinates) and structure data (e.g., structure names, masks).

The scripts adopt the [PortPy](https://github.com/cqad/PortPy) data format, where:

-   Light-weight metadata is stored in human-readable `.json` files.
    
-   Large datasets (e.g., dose influence matrices) are stored in `.h5` (HDF5) files.
    

A typical output folder structure for a patient might look like this:

```
│
├── Beams/
│   ├── Beam_0_MetaData.json
│   ├── Beam_0_Data.h5
│   ├── Beam_1_MetaData.json
│   ├── Beam_1_Data.h5
├── CT_Data.h5
├── CT_MetaData.json
└── StructureSet_MetaData.json
└── StructureSet_Data.h5

```
#### Example JSON and HDF5 Files

``` Beam_0_metadata.json ```
Below is an example `.json` file for a beam. Notice how the larger data arrays (e.g., beamlets, influence matrices) point to external `.h5` files with specific tags. For instance, ``` "influenceMatrixSparse_File": "Beam_0_Data.h5/inf_matrix_sparse"```, means the influence matrix is stored in a file named  *Beam_0_Data.h5* under a tag named *inf_matrix_sparse*. 
  
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

```Beam_0_Data.h5 ```
HDF5 (Hierarchical Data Format version 5) is a common and powerful format that is supported by most programming languages. It is designed to store and organize large amounts of complex data using a flexible, hierarchical structure, allowing efficient access, compression, and storage of multidimensional arrays. The following example shows the hierarchical data for a beam. [HDFViwer](https://www.hdfgroup.org/downloads/hdfview/) can be used to see through a .h5 file. 
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

# How to read the data? 
**Python users** can use [PortPy](https://github.com/cqad/PortPy) to parse the JSON/HDF5 files into Python dictionaries and objects that can be easilly used in python. **Other languages** (e.g., C#) will require you to parse `.json` and `.h5` files in your own code.

The following snippet shows how PortPy can be used for loading the data, performing planning optimization, and visulaization. See [PortPy Tutorial](https://github.com/PortPy-Project/PortPy/blob/master/examples/1_basic_tutorial.ipynb) for more details.

```
import portpy as pp
# Use PortPy DataExplorer class to  load the data
data = pp.DataExplorer(data_dir='../data')
ct = pp.CT(data)
structs = pp.Structures(data)
beams = pp.Beams(data)
inf_matrix = pp.InfluenceMatrix(ct=ct, structs=structs, beams=beams)
# create a plan object
my_plan = pp.Plan(ct = ct, structs = structs, beams = beams, inf_matrix = inf_matrix, clinical_criteria=clinical_criteria)
# solve the optimization problem
opt = pp.Optimization(my_plan, opt_params=opt_params, clinical_criteria=clinical_criteria)
opt.create_cvxpy_problem()
sol = opt.solve(solver='MOSEK', verbose=False)
# Visulaization:
# plot dvh for the structures
pp.Visualization.plot_dvh(my_plan, sol=sol, struct_names=['PTV', 'CORD'], title=data.patient_id)
# plot 2d axial slice for the given solution and display the structures contours on the slice
pp.Visualization.plot_2d_slice(my_plan=my_plan, sol=sol, slice_num=60, struct_names=['PTV'])
```


## How to run the scripts?

You can run the program using one of the following methods:

#### 1. Eclipse plugin

1.  **Locate the plugin:**  
    Go to the [Plugin Directory](https://github.com/Varian-MedicalAffairsAppliedSolutions/MAAS-DoseInfluenceMatrix/tree/main/PhotonDoseCalc/Plugin/bin/Release).
    
2.  **Edit the configuration file:**  
    Open [PhotonInfluenceMatrixCalcPlugin.esapi.config](https://github.com/Varian-MedicalAffairsAppliedSolutions/MAAS-DoseInfluenceMatrix/blob/main/PhotonDoseCalc/Source_C%23/bin/release/PhotonInfluenceMatrixCalcPlugin.esapi.config) and set:
    
    -   `OutputRootFolder` to your desired output directory.
        
    -   `BeamletSizeX` and `BeamletSizeY` to customize the beamlet size.
        
    -   `EclipseVolumeDoseCalcModel` to match your dose calculation version.
        
3.  **Run in Eclipse:**
    
    -   Open the desired patient plan (e.g., **Plan1**).
        
    -   Go to **Tools → Scripts → Change Folder** and select the plugin directory from step 1.
        
    -   Click **Open**, select **PhotonInfluenceMatrixCalcPlugin.esapi.dll**, and click **Run**.
        
    -   (Optional) Add this plugin to **Favorites** for quicker access.
        

**Result:** The program outputs data to the `OutputRootFolder` specified in the config.

### 2. Command-Line (Standalone)

If you have access to an Eclipse thick-client workstation, you can run the script standalone:

1.  **Locate the executable:**  
    Go to the [Executable Directory](https://github.com/Varian-MedicalAffairsAppliedSolutions/MAAS-DoseInfluenceMatrix/tree/main/PhotonDoseCalc/Source_C%23/bin/release).
    
2.  **Edit the configuration file:**  
    Open [PhotonInfluenceMatrixCalc.exe.config](https://github.com/Varian-MedicalAffairsAppliedSolutions/MAAS-DoseInfluenceMatrix/blob/main/PhotonDoseCalc/Source_C%23/bin/release/PhotonInfluenceMatrixCalc.exe.config) and set:
    
    -   `OutputRootFolder` to your desired output directory.
        
    -   `BeamletSizeX` and `BeamletSizeY` to customize beamlet size.
        
    -   `EclipseVolumeDoseCalcModel` to match your dose calculation version.
        
3.  **Run the program:**  
    From a command prompt or terminal, execute: 
   ```bash
       PhotonInfluenceMatrixCalc.exe <patient_mrn> <course_name> <plan_name>
```

