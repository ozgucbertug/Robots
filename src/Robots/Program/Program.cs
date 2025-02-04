﻿using Rhino.Geometry;
using System.Text.RegularExpressions;
using static System.Math;

namespace Robots;

public interface IProgram
{
    string Name { get; }
    RobotSystem RobotSystem { get; }
    List<List<List<string>>>? Code { get; }
    bool HasSimulation { get; }
    List<int> MultiFileIndices { get; }
    void Save(string folder);
}

public class Program : IProgram
{
    // static 
    public static bool IsValidIdentifier(string name, out string error)
    {
        if (name.Length == 0)
        {
            error = "name is empty.";
            return false;
        }

        var excess = name.Length - 32;

        if (excess > 0)
        {
            error = $"name is {excess} character(s) too long.";
            return false;
        }

        if (!char.IsLetter(name[0]))
        {
            error = "name must start with a letter.";
            return false;
        }

        if (!Regex.IsMatch(name, @"^[A-Z0-9_]+$", RegexOptions.IgnoreCase))
        {
            error = "name can only contain letters, digits, and underscores (_).";
            return false;
        }

        error = "";
        return true;
    }

    // instance

    readonly Simulation? _simulation;

    public string Name { get; }
    public RobotSystem RobotSystem { get; }
    public List<CellTarget> Targets { get; }
    public List<int> MultiFileIndices { get; }
    public List<TargetAttribute> Attributes { get; } = new List<TargetAttribute>();
    public List<Command> InitCommands { get; }
    public List<string> Warnings { get; } = new List<string>();
    public List<string> Errors { get; } = new List<string>();
    public List<List<List<string>>>? Code { get; }
    public double Duration { get; internal set; }

    public IMeshPoser? MeshPoser { get; set; }
    public SimulationPose CurrentSimulationPose => _simulation?.CurrentSimulationPose
        ?? throw new InvalidOperationException(" This program cannot be animated.");

    public bool HasSimulation => _simulation is not null;

    public Program(string name, RobotSystem robotSystem, IEnumerable<IToolpath> toolpaths, Commands.Group? initCommands = null, IEnumerable<int>? multiFileIndices = null, double stepSize = 1.0)
    {
        RobotSystem = robotSystem;
        InitCommands = initCommands?.Flatten().ToList() ?? new List<Command>(0);
        var targets = CreateCellTargets(toolpaths);

        if (targets.Count > 0)
        {
            var checkProgram = new CheckProgram(this, targets, stepSize);
            _simulation = new Simulation(this, checkProgram.Keyframes);
            targets = checkProgram.FixedTargets;
        }

        Targets = targets;

        Name = name;
        CheckName(name, robotSystem);
        MultiFileIndices = FixMultiFileIndices(multiFileIndices, Targets.Count);

        if (Errors.Count == 0)
            Code = RobotSystem.Code(this);
    }

    void CheckName(string name, RobotSystem robotSystem)
    {
        if (robotSystem is RobotCell cell)
        {
            var group = cell.MechanicalGroups.MaxBy(g => g.Name.Length).Name;
            name = $"{name}_{group}_000";
        }

        if (!IsValidIdentifier(name, out var error))
            Errors.Add("Program " + error);

        if (robotSystem is RobotCellKuka)
        {
            var excess = name.Length - 24;

            if (excess > 0)
                Warnings.Add($"If using an older KRC2 or KRC3 controller, make the program name {excess} character(s) shorter.");
        }
    }

    List<CellTarget> CreateCellTargets(IEnumerable<IToolpath> toolpaths)
    {
        var cellTargets = new List<CellTarget>();
        var enumerators = toolpaths.Select(e => e.Targets.GetEnumerator()).ToList();

        if (RobotSystem is RobotCell cell)
        {
            var pathsCount = enumerators.Count;
            var groupCount = cell.MechanicalGroups.Count;

            if (pathsCount != groupCount)
            {
                Errors.Add($"You supplied {pathsCount} toolpath(s), this robot cell requires {groupCount} toolpath(s).");
                goto End;
            }
        }

        while (enumerators.All(e => e.MoveNext()))
        {
            var programTargets = new List<ProgramTarget>(enumerators.Count);
            programTargets.AddRange(enumerators.Select((e, i) => new ProgramTarget(e.Current, i)));

            if (programTargets.Any(t => t.Target is null))
            {
                Errors.Add($"Target index {cellTargets.Count} is null or invalid.");
                goto End;
            }

            var cellTarget = new CellTarget(programTargets, cellTargets.Count);
            cellTargets.Add(cellTarget);
        }

        if (enumerators.Any(e => e.MoveNext()))
        {
            Errors.Add("All toolpaths must contain the same number of targets.");
            goto End;
        }

        if (cellTargets.Count == 0)
        {
            Errors.Add("The program must contain at least 1 target.");
        }

    End:
        return cellTargets;
    }

    List<int> FixMultiFileIndices(IEnumerable<int>? multiFileIndices, int targetCount)
    {
        if (Errors.Any())
            return new List<int> { 0 };

        var indices = multiFileIndices?.ToList() ?? new List<int> { 0 };

        if (indices.Count > 0)
        {
            int startCount = indices.Count;
            indices = indices.Where(i => i < targetCount).ToList();

            if (startCount > indices.Count)
                Warnings.Add("Multi-file index was higher than the number of targets.");

            indices.Sort();
        }

        if (indices.Count == 0 || indices[0] != 0)
            indices.Insert(0, 0);

        return indices;
    }

    public IProgram CustomCode(List<List<List<string>>> code) => new CustomProgram(Name, RobotSystem, MultiFileIndices, code);

    public void Animate(double time, bool isNormalized = true)
    {
        if (_simulation is null)
            return;

        _simulation.Step(time, isNormalized);

        if (MeshPoser is null)
            return;

        var current = _simulation.CurrentSimulationPose;
        var cellTarget = Targets[current.TargetIndex];

        MeshPoser.Pose(current.Kinematics, cellTarget);
    }

    public Collision CheckCollisions(IEnumerable<int>? first = null, IEnumerable<int>? second = null, Mesh? environment = null, int environmentPlane = 0, double linearStep = 100, double angularStep = PI / 4.0)
    {
        return new Collision(this, first ?? new int[] { 7 }, second ?? new int[] { 4 }, environment, environmentPlane, linearStep, angularStep);
    }

    public void Save(string folder) => RobotSystem.SaveCode(this, folder);

    public override string ToString()
    {
        int seconds = (int)Duration;
        int milliseconds = (int)((Duration - (double)seconds) * 1000);
        string format = @"hh\:mm\:ss";
        var span = new TimeSpan(0, 0, 0, seconds, milliseconds);
        return $"Program ({Name} with {Targets.Count} targets and {span.ToString(format)} (h:m:s) long)";
    }
}
