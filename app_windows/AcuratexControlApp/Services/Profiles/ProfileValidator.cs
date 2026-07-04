using AcuratexControlApp.Models.Profiles;

namespace AcuratexControlApp.Services.Profiles;

public sealed record ProfileValidationResult(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

public sealed class ProfileValidationException : InvalidOperationException
{
    public ProfileValidationException(IReadOnlyList<string> errors)
        : base(string.Join(Environment.NewLine, errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}

public sealed class ProfileValidator
{
    private const uint MaxCanId = 0x1FFFFFFF;

    public ProfileValidationResult ValidateCreateRequest(ProfileCreateRequest request)
    {
        List<string> errors = new();
        List<string> warnings = new();

        ValidateProfileKey(request.ProfileKey, errors);
        ValidateDisplayName(request.DisplayName, errors);
        if (request.ProgramNumber.HasValue && request.ProgramNumber.Value < 1) {
            errors.Add("Program number must be greater than zero when present.");
        }

        return BuildResult(errors, warnings);
    }

    public ProfileValidationResult ValidateVersionWriteRequest(ProfileVersionWriteRequest request)
    {
        List<string> errors = new();
        List<string> warnings = new();

        if (request.VersionNumber.HasValue && request.VersionNumber.Value < 1) {
            errors.Add("Version number must be greater than zero when present.");
        }

        if (request.SchemaVersion < 1) {
            errors.Add("Schema version must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.Notes)) {
            warnings.Add("Version notes are empty.");
        }

        ValidateCommands(request.Commands, errors, warnings);
        return BuildResult(errors, warnings);
    }

    public void ValidateCreateRequestOrThrow(ProfileCreateRequest request)
    {
        ThrowIfInvalid(ValidateCreateRequest(request));
    }

    public void ValidateVersionWriteRequestOrThrow(ProfileVersionWriteRequest request)
    {
        ThrowIfInvalid(ValidateVersionWriteRequest(request));
    }

    public void ValidateCommandsOrThrow(HeadCommandProfileModel commands)
    {
        ThrowIfInvalid(ValidateCommands(commands));
    }

    public ProfileValidationResult ValidateCommands(HeadCommandProfileModel commands)
    {
        List<string> errors = new();
        List<string> warnings = new();
        ValidateCommands(commands, errors, warnings);
        return BuildResult(errors, warnings);
    }

    private static void ValidateCommands(HeadCommandProfileModel commands, ICollection<string> errors, ICollection<string> warnings)
    {
        if (commands is null) {
            errors.Add("Profile commands cannot be null.");
            return;
        }

        if (commands.ProgramNumber < 1) {
            errors.Add("Program number must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(commands.ProgramName)) {
            errors.Add("Program name cannot be empty.");
        }

        ValidateInitSequence(commands.InitSequence, errors);
        ValidateTesteo(commands.Testeo, errors);
        ValidateMotion(commands.Den, errors, warnings);
        ValidateMotion(commands.Sic, errors, warnings);
        ValidateMotion(commands.Feet, errors, warnings);
        ValidateJ(commands.J, errors);
        ValidateCascade(commands.Yarn, errors);
        ValidateCascade(commands.Stitch, errors);
        ValidateStop(commands.Stop, errors);
    }

    private static void ValidateInitSequence(HeadInitCommandSequenceModel sequence, ICollection<string> errors)
    {
        if (sequence is null) {
            errors.Add("INIT sequence cannot be null.");
            return;
        }

        if (sequence.Phase1StepCount == 0 || sequence.Phase2StepCount == 0) {
            errors.Add("INIT sequence must contain both phase 1 and phase 2 steps.");
        }

        foreach (HeadInitStepModel step in sequence.Phase1Steps.Concat(sequence.Phase2Steps)) {
            ValidateInitStep(step, errors);
        }
    }

    private static void ValidateInitStep(HeadInitStepModel step, ICollection<string> errors)
    {
        if (step is null) {
            errors.Add("INIT step cannot be null.");
            return;
        }

        if (step.Phase is not (1 or 2)) {
            errors.Add($"INIT step '{step.RawText}' has invalid phase '{step.Phase}'.");
        }

        if (step.StepOrder < 0) {
            errors.Add($"INIT step '{step.RawText}' has invalid order.");
        }

        if (string.IsNullOrWhiteSpace(step.RawText)) {
            errors.Add("INIT step raw text cannot be empty.");
        }

        switch (step.StepKind) {
            case HeadInitStepKind.Can:
                if (step.Bus is not (1 or 2)) {
                    errors.Add($"INIT CAN step '{step.RawText}' must declare bus 1 or 2.");
                }

                if (!step.CanId.HasValue || step.CanId.Value > MaxCanId) {
                    errors.Add($"INIT CAN step '{step.RawText}' has invalid CAN id.");
                }

                if (!step.Dlc.HasValue || step.Dlc.Value > 8) {
                    errors.Add($"INIT CAN step '{step.RawText}' has invalid DLC.");
                }

                if (step.Data is null) {
                    errors.Add($"INIT CAN step '{step.RawText}' is missing data.");
                } else if (step.Dlc.HasValue && step.Data.Length != step.Dlc.Value) {
                    errors.Add($"INIT CAN step '{step.RawText}' has a DLC/data length mismatch.");
                }

                break;
            case HeadInitStepKind.Wait:
                if (!step.WaitMs.HasValue || step.WaitMs.Value < 0) {
                    errors.Add($"INIT WAIT step '{step.RawText}' has invalid delay.");
                }

                break;
            case HeadInitStepKind.Status:
                break;
            default:
                errors.Add($"INIT step '{step.RawText}' has unsupported step kind.");
                break;
        }
    }

    private static void ValidateTesteo(HeadTesteoCommandProfileModel testeo, ICollection<string> errors)
    {
        if (testeo is null) {
            errors.Add("TESTEO profile cannot be null.");
            return;
        }

        ValidateCanCommand(testeo.Ping, "TESTEO ping", errors);
        ValidateCanCommand(testeo.Reset, "TESTEO reset", errors);

        if (testeo.ResponseCanId > MaxCanId) {
            errors.Add("TESTEO response CAN id is invalid.");
        }

        if (testeo.ResetCanId > MaxCanId) {
            errors.Add("TESTEO reset CAN id is invalid.");
        }

        if (testeo.MaxTries == 0) {
            errors.Add("TESTEO max tries must be greater than zero.");
        }
    }

    private static void ValidateMotion(HeadMotionCommandProfileModel motion, ICollection<string> errors, ICollection<string> warnings)
    {
        if (motion is null) {
            errors.Add("Motion profile cannot be null.");
            return;
        }

        if (motion.CanId > MaxCanId) {
            errors.Add($"Motion profile '{motion.ModuleKind}' has invalid CAN id.");
        }

        if (motion.InstanceCount < 0) {
            errors.Add($"Motion profile '{motion.ModuleKind}' has invalid instance count.");
        }



        bool isEmptyModule = motion.InstanceCount == 0
            && motion.RunSequenceCount == 0
            && motion.PositionCount == 0
            && motion.AlternateRunSequenceCount == 0;

        if (!isEmptyModule) {
            if (motion.RunSequenceCount == 0) {
                errors.Add($"Motion profile '{motion.ModuleKind}' must contain a run sequence.");
            }

            if (motion.PositionCount == 0) {
                errors.Add($"Motion profile '{motion.ModuleKind}' must contain positions.");
            }

            for (int i = 0; i < motion.RunSequenceCount; i++) {
                ValidatePositionReference(motion.RunSequence[i], motion.PositionCount, $"motion '{motion.ModuleKind}' run sequence", errors);
            }

            for (int i = 0; i < motion.AlternateRunSequenceCount; i++) {
                ValidatePositionReference(motion.AlternateRunSequence[i], motion.PositionCount, $"motion '{motion.ModuleKind}' alternate sequence", errors);
            }
        }

        if (motion.ModuleKind == HeadMotionModuleKind.Feet && !isEmptyModule) {
            warnings.Add("Feet profile is configured; the imported P1 seed keeps it empty.");
        }
    }

    private static void ValidateJ(HeadJCommandProfileModel jProfile, ICollection<string> errors)
    {
        if (jProfile is null) {
            errors.Add("J profile cannot be null.");
            return;
        }

        if (jProfile.CanId > MaxCanId) {
            errors.Add("J profile has invalid CAN id.");
        }

        if (jProfile.InstanceCount < 0) {
            errors.Add("J profile has invalid instance count.");
        }

        if (jProfile.ChannelCount > 8) {
            errors.Add("J profile has invalid channel count.");
        }
    }

    private static void ValidateCascade(HeadCascadeCommandProfileModel cascade, ICollection<string> errors)
    {
        if (cascade is null) {
            errors.Add("Cascade profile cannot be null.");
            return;
        }

        if (cascade.CanId > MaxCanId) {
            errors.Add($"Cascade profile '{cascade.ModuleKind}' has invalid CAN id.");
        }

        if (cascade.AddressesPerInstance == 0) {
            errors.Add($"Cascade profile '{cascade.ModuleKind}' must have at least one address per instance.");
        }

        if (cascade.InstanceCount < 0) {
            errors.Add($"Cascade profile '{cascade.ModuleKind}' has invalid instance count.");
        }

        int expectedAddressCount = cascade.AddressesPerInstance * cascade.InstanceCount;
        if (expectedAddressCount != cascade.AddressCount) {
            errors.Add($"Cascade profile '{cascade.ModuleKind}' address count mismatch.");
        }
    }

    private static void ValidateStop(HeadStopCommandProfileModel stop, ICollection<string> errors)
    {
        if (stop is null) {
            errors.Add("STOP profile cannot be null.");
            return;
        }

        if (stop.SendsCanFrame) {
            ValidateCanCommand(stop.Frame, "STOP frame", errors);
        } else if (stop.Frame is not null && stop.Frame.Data.Length > 0) {
            errors.Add("STOP frame must be empty when sends_can_frame is false.");
        }
    }

    private static void ValidateCanCommand(HeadCanCommandModel? frame, string label, ICollection<string> errors)
    {
        if (frame is null) {
            errors.Add($"{label} cannot be null.");
            return;
        }

        if (frame.CanId > MaxCanId) {
            errors.Add($"{label} has invalid CAN id.");
        }

        if (frame.Dlc > 8) {
            errors.Add($"{label} has invalid DLC.");
        }

        if (frame.Data is null) {
            errors.Add($"{label} has null data.");
            return;
        }

        if (frame.Data.Length != frame.Dlc) {
            errors.Add($"{label} has a DLC/data length mismatch.");
        }
    }

    private static void ValidateProfileKey(string profileKey, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(profileKey)) {
            errors.Add("Profile key cannot be empty.");
        }
    }

    private static void ValidateDisplayName(string displayName, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(displayName)) {
            errors.Add("Display name cannot be empty.");
        }
    }

    private static void ValidatePositionReference(byte positionNumber, int positionCount, string label, ICollection<string> errors)
    {
        if (positionNumber == 0 || positionNumber > positionCount) {
            errors.Add($"{label} references invalid position number '{positionNumber}'.");
        }
    }

    private static ProfileValidationResult BuildResult(List<string> errors, List<string> warnings)
    {
        return new ProfileValidationResult(errors.Count == 0, errors, warnings);
    }

    private static void ThrowIfInvalid(ProfileValidationResult result)
    {
        if (!result.IsValid) {
            throw new ProfileValidationException(result.Errors);
        }
    }
}

