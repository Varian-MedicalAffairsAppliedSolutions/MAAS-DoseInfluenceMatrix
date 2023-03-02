Script for calculating the influence matrix of each spot in the raw spot list of a plan. The script only supports plans with exactly one field.
Script usage:
.\CalculateInfluenceMatrix.exe <patientId> <courseId> <planId>
Alternatively if no arguments are provided, the user is prompted to input the plan information.

The logs from the calculation are created in the "logs" directory.
The results are written into comma separated value files in the "results" directory that is created to the directory from which the script is run.
A directory is created for the results of the plan that was used. The results only contain information about the voxels where the dose is non-zero. 