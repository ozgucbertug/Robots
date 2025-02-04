﻿using Rhino.Geometry;

namespace Robots.Grasshopper;

public class FromPlane : GH_Component
{
    public FromPlane() : base("From plane", "FromPlane", "Returns a list of numbers from a plane. The first 3 numbers are the x, y, and z coordinates of the origin. The last 3 or 4 values correspond to Euler angles in degrees or quaternion values respectively.", "Robots", "Utility") { }
    public override GH_Exposure Exposure => GH_Exposure.primary;
    public override Guid ComponentGuid => new("{03353E74-E816-4E0A-AF9A-8AFB4C111D0B}");
    protected override System.Drawing.Bitmap Icon => Util.GetIcon("iconToPlane");

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddPlaneParameter("Plane", "P", "Plane to convert to Euler, quaternion or axis angle values.", GH_ParamAccess.item);
        pManager.AddParameter(new RobotSystemParameter(), "Robot system", "R", "The robot system will select the orientation type (ABB = quaternions, KUKA = Euler angles in degrees, UR = axis angles in radians). If this input is left unconnected, the 3D rotation will be expressed in quaternions.", GH_ParamAccess.item);
        pManager[1].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddNumberParameter("Numbers", "N", "The first 3 numbers are the x, y and z coordinates of the origin. The last 3 or 4 numbers represent a 3D rotation.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        Plane plane = default;
        RobotSystem? robotSystem = null;

        if (!DA.GetData(0, ref plane)) return;
        DA.GetData(1, ref robotSystem);

        var numbers = robotSystem is null
            ? RobotCellAbb.PlaneToQuaternion(plane)
            : robotSystem.PlaneToNumbers(plane);

        DA.SetDataList(0, numbers);
    }
}
