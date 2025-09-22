# MAAS-DoseInfluenceMatrix 

## Introduction 
This repository contains ESAPI scripts to extract all data required for performing treatment planning optimization outside the Eclipse environment. An Eclipse instance is required to extract the data. Once the data is obtained, a treatment planning optimization software, such as [PortPy](https://github.com/PortPy-Project/PortPy), can load the data and perform the planning optimization. This approach provides flexibility to researchers who want to develop novel treatment planning optimization techniques that are not readily available within ESAPI.

**Note:** *Only the photon version is currently working. The proton version is a work in progress. The script loops through each field, subdivides the beam into small beamlets (or spots in proton mode), and calculates dose for each beamlet. Hence, the process can be computationally demanding and time-consuming.*

## Data and data format
Each beam is divided into small 2D beamlets/spots, and the patient’s body is divided into small 3D voxels. Eclipse is used to calculate the dose contribution of each beamlet to every voxel, resulting in a **dose influence matrix** (also called a dose deposition matrix or dij matrix). Relevant beamlet and voxel information (e.g., size, coordinates) is stored, as well as CT data (e.g., voxel Hounsfield Units, coordinates) and structure data (e.g., structure names, masks).

This tool adopts an output data format, where:

-   Light-weight metadata is stored in human-readable `.json` files.
    
-   Large datasets (e.g., dose influence matrices) are stored in natively compressed `.h5` (HDF5) files.
    

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

##### Beam_0_metadata.json
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

##### Beam_0_Data.h5
HDF5 (Hierarchical Data Format version 5) is a common and powerful format that is supported by most programming languages. It is designed to store and organize large amounts of complex data using a flexible, hierarchical structure, allowing efficient access, compression, and storage of multidimensional arrays. The following example shows the hierarchical data for a beam. [HDFViwer](https://www.hdfgroup.org/downloads/hdfview/) can be used to view a .h5 file. 
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
**Python users** can use PortPy to parse the JSON/HDF5 files into Python dictionaries and objects that can be easilly used in python. **Other languages** (e.g., C#) will require you to parse `.json` and `.h5` files in your own code.

The following snippet shows how PortPy can be used for loading the data, performing planning optimization, and visulaization. See [PortPy Tutorial](https://github.com/PortPy-Project/PortPy/blob/master/examples/1_basic_tutorial.ipynb) for more details.

```Python 
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

#### **1. Using the Eclipse Plugin (GUI Method)**
For users unfamiliar with command-line scripting, the plugin provides a **graphical interface** for dose calculation.

##### **Steps:**
1. Navigate to the release [tab](https://github.com/Varian-MedicalAffairsAppliedSolutions/MAAS-DoseInfluenceMatrix/releases)  for dose calculation module and download the CalculateInfluenceMatrix package that matches with your Eclipse version. 
2. Modify the config file:  
   - Edit CalculateInfluenceMatrix.esapi.dll.config.  
   - Set `Photon_OutputRootFolder` to your preferred output directory.  You can also modify `BeamletSizeX` and `BeamletSizeY` for choosing the beamlet size in X and Y direction. Please modify the `Photon_EclipseVolumeDoseCalcModel` based on your dose calculation version available at your institution.
3. In Eclipse:
   - Open your patient plan (e.g., **PortPy_Plan**).  
   - Navigate to **Tools → Scripts → Change Folder**.  
   - Change the path to downloaded folder from release and click **Open**.  
   - Select **CalculateInfluenceMatrix.esapi.dll** and click **Run**
   - (Optional) Add the plugin to **Favorites** for quicker access.  

**Output:** PortPy-compatible data will be saved in the `OutputRootFolder` specified in the config file.


#### **2. Using the Executable (Command-Line Method)**
For users comfortable with command-line execution.

##### **Steps:**
1. Navigate to the release [tab](https://github.com/Varian-MedicalAffairsAppliedSolutions/MAAS-DoseInfluenceMatrix/releases)  for dose calculation module and download the CalculateInfluenceMatrix package that matches with your Eclipse version.
2. Modify the config file:
   - Edit CalculateInfluenceMatrix.exe.config.  
   - Set `Photon_OutputRootFolder` to your preferred output directory. You can also modify `Photon_BeamletSizeX` and `Photon_BeamletSizeY` for choosing the beamlet size in X and Y direction. Please modify the `Photon_EclipseVolumeDoseCalcModel` based on your dose calculation version available at your institution.
     
3. Run the following command in a terminal or command prompt:  
   ```bash
   CalculateInfluenceMatrix.exe <patient_mrn> <course_name> <plan_name>

**Output:** PortPy-compatible data will be saved in the `OutputRootFolder` specified in the config file.
