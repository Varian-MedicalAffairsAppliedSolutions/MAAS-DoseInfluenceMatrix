using System;
using System.Collections.Generic;

namespace CalculateInfluenceMatrix
{
  public class DosePoint
  {
    public DosePoint() { }
    public DosePoint(int x, int y, int slice, double dose)
    {
      indexX = x;
      indexY = y;
      sliceIndex = slice;
      doseValue = dose;
    }
    public int indexX = 0;
    public int indexY = 0;
    public int sliceIndex = 0;
    public double doseValue = 0;
  }

  public class DoseData
  {
    public DoseData() { }
    public DoseData(List<DosePoint> points)
    {
      dosePoints = points;
    }
    public List<DosePoint> dosePoints = new List<DosePoint>();
  }
}
