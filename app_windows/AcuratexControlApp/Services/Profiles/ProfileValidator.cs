using System.Text;
using AcuratexControlApp.Models.Profiles;
using AcuratexControlApp.Services.Profiles.Acx;

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
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private const uint MaxCanId = 0x1FFFFFFF;

    public ProfileValidationResult ValidateCreateRequest(ProfileCreateRequest request)
    {
        List<string> errors = new();
        List<string> warnings = new();

        ValidateProfileKey(request.ProfileKey, errors);
        ValidateDisplayName(request.DisplayName, errors);
        ValidateTextLength(request.Description, AcxFormatConstants.MaxMetadataStringBytes, "Description", errors);
        if (request.ProgramNumber.HasValue && (request.ProgramNumber.Value < 1 || request.ProgramNumber.Value > 3)) {
            errors.Add("Program number must be between 1 and 3 when present.");
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

        ValidateTextLength(request.Notes, AcxFormatConstants.MaxMetadataStringBytes, "Notes", errors);
        if (string.IsNullOrWhiteSpace(request.Notes)) {
            warnings.Add("Version notes are empty.");
        }

        ValidateCommands(request.Commands, errors, warnings);
        ValidateActions(request.Actions, errors, warnings);
        return BuildResult(errors, warnings);
    }

    public ProfileValidationResult ValidateVersionDocument(ProfileVersionDocument document)
    {
        List<string> errors = new();
        List<string> warnings = new();

        if (document is null) {
            errors.Add("Profile version document cannot be null.");
            return BuildResult(errors, warnings);
        }

        ValidateProfileRecord(document.Profile, document.Commands, errors);
        ValidateVersionSummary(document.Version, document.Profile, errors);
        ValidateCommands(document.Commands, errors, warnings);
        ValidateActions(document.Actions, errors, warnings);
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

    public void ValidateVersionDocumentOrThrow(ProfileVersionDocument document)
    {
        ThrowIfInvalid(ValidateVersionDocument(document));
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

    private static void ValidateProfileRecord(ProfileRecord profile, HeadCommandProfileModel commands, ICollection<string> errors)
    {
        if (profile is null) {
            errors.Add("Profile metadata cannot be null.");
            return;
        }

        ValidateProfileKey(profile.ProfileKey, errors);
        ValidateDisplayName(profile.DisplayName, errors);
        ValidateTextLength(profile.Description, AcxFormatConstants.MaxMetadataStringBytes, "Description", errors);
        if (!profile.ProgramNumber.HasValue) {
            errors.Add("Profile program number is required for ACX export.");
        } else if (profile.ProgramNumber.Value < 1 || profile.ProgramNumber.Value > 3) {
            errors.Add("Profile program number must be between 1 and 3.");
        }

        if (commands is not null && profile.ProgramNumber.HasValue && profile.ProgramNumber.Value != commands.ProgramNumber) {
            errors.Add("Profile program number does not match the command profile program number.");
        }
    }

    private static void ValidateVersionSummary(ProfileVersionSummary version, ProfileRecord profile, ICollection<string> errors)
    {
        if (version is null) {
            errors.Add("Profile version metadata cannot be null.");
            return;
        }

        if (version.Id < 0 || version.Id > uint.MaxValue) {
            errors.Add($"Profile version id '{version.Id}' is outside the ACX uint32 range.");
        }

        if (profile is not null && version.ProfileId != profile.Id) {
            errors.Add("Profile version profile id does not match the parent profile id.");
        }

        if (version.VersionNumber < 1) {
            errors.Add("Profile version number must be greater than zero.");
        }

        if (version.SchemaVersion < 1) {
            errors.Add("Profile version schema version must be greater than zero.");
        }

        ValidateTextLength(version.Notes, AcxFormatConstants.MaxMetadataStringBytes, "Notes", errors);
    }

    private static void ValidateCommands(HeadCommandProfileModel commands, ICollection<string> errors, ICollection<string> warnings)
    {
        if (commands is null) {
            errors.Add("Profile commands cannot be null.");
            return;
        }

        if (commands.ProgramNumber < 1 || commands.ProgramNumber > 3) {
            errors.Add("Program number must be between 1 and 3.");
        }

        ValidateTextLength(commands.ProgramName, AcxFormatConstants.MaxProfileDisplayNameUtf8Bytes, "Program name", errors);
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

        if (sequence.Phase1StepCount > AcxFormatConstants.MaxInitStepsPerPhase) {
            errors.Add($"INIT phase 1 exceeds the maximum of {AcxFormatConstants.MaxInitStepsPerPhase} steps.");
        }

        if (sequence.Phase2StepCount > AcxFormatConstants.MaxInitStepsPerPhase) {
            errors.Add($"INIT phase 2 exceeds the maximum of {AcxFormatConstants.MaxInitStepsPerPhase} steps.");
        }

        ValidateSequentialInitSteps(sequence.Phase1Steps, 1, errors);
        ValidateSequentialInitSteps(sequence.Phase2Steps, 2, errors);
    }

    private static void ValidateSequentialInitSteps(IReadOnlyList<HeadInitStepModel> steps, int phase, ICollection<string> errors)
    {
        for (int i = 0; i < steps.Count; i++) {
            ValidateInitStep(steps[i], phase, i, errors);
        }
    }

    private static void ValidateInitStep(HeadInitStepModel step, int expectedPhase, int expectedOrder, ICollection<string> errors)
    {
        if (step is null) {
            errors.Add("INIT step cannot be null.");
            return;
        }

        if (step.Phase != expectedPhase) {
            errors.Add($"INIT step '{step.RawText}' has invalid phase '{step.Phase}'.");
        }

        if (step.StepOrder != expectedOrder) {
            errors.Add($"INIT step '{step.RawText}' is out of order. Expected '{expectedOrder}'.");
        }

        ValidateTextLength(step.RawText, AcxFormatConstants.MaxLineUtf8Bytes, "INIT step", errors);

        switch (step.StepKind) {
            case HeadInitStepKind.Can:
                if (step.Bus is not (1 or 2)) {
                    errors.Add($"INIT CAN step '{step.RawText}' must declare bus 1 or 2.");
                }

                if (!step.CanId.HasValue || step.CanId.Value > MaxCanId) {
                    errors.Add($"INIT CAN step '{step.RawText}' has invalid CAN id.");
                }

                if (!step.Dlc.HasValue || step.Dlc.Value > AcxFormatConstants.MaxCanDlc) {
                    errors.Add($"INIT CAN step '{step.RawText}' has invalid DLC.");
                }

                if (step.Data is null) {
                    errors.Add($"INIT CAN step '{step.RawText}' is missing data.");
                } else if (step.Dlc.HasValue && step.Data.Length != step.Dlc.Value) {
                    errors.Add($"INIT CAN step '{step.RawText}' has a DLC/data length mismatch.");
                }

                if (step.WaitMs.HasValue) {
                    errors.Add($"INIT CAN step '{step.RawText}' must not set a wait time.");
                }

                break;
            case HeadInitStepKind.Wait:
                if (step.Bus.HasValue || step.CanId.HasValue || step.Dlc.HasValue || step.Data is not null) {
                    errors.Add($"INIT WAIT step '{step.RawText}' has CAN fields set.");
                }

                if (!step.WaitMs.HasValue || step.WaitMs.Value < 0 || step.WaitMs.Value > (int)AcxFormatConstants.MaxWaitMilliseconds) {
                    errors.Add($"INIT WAIT step '{step.RawText}' has invalid delay.");
                }

                break;
            case HeadInitStepKind.Status:
                if (step.Bus.HasValue || step.CanId.HasValue || step.Dlc.HasValue || step.Data is not null || step.WaitMs.HasValue) {
                    errors.Add($"INIT STATUS step '{step.RawText}' must not contain CAN or wait fields.");
                }

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

        if (testeo.ResponseTimeoutMs > AcxFormatConstants.MaxWaitMilliseconds
            || testeo.RetryDelayMs > AcxFormatConstants.MaxWaitMilliseconds
            || testeo.ResetDebounceMs > AcxFormatConstants.MaxWaitMilliseconds) {
            errors.Add("TESTEO timing values exceed the maximum supported wait time.");
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

        int maxInstances = motion.ModuleKind switch
        {
            HeadMotionModuleKind.Den => AcxFormatConstants.MaxDenInstanceCount,
            HeadMotionModuleKind.Sic => AcxFormatConstants.MaxSicInstanceCount,
            HeadMotionModuleKind.Feet => AcxFormatConstants.MaxFeetInstanceCount,
            _ => 0,
        };

        if (motion.InstanceCount > maxInstances) {
            errors.Add($"Motion profile '{motion.ModuleKind}' exceeds the maximum instance count of {maxInstances}.");
        }

        if (motion.PositionCount > AcxFormatConstants.MaxPositions) {
            errors.Add($"Motion profile '{motion.ModuleKind}' exceeds the maximum position count of {AcxFormatConstants.MaxPositions}.");
        }

        if (motion.RunSequenceCount > AcxFormatConstants.MaxMotionSequenceLength || motion.AlternateRunSequenceCount > AcxFormatConstants.MaxMotionSequenceLength) {
            errors.Add($"Motion profile '{motion.ModuleKind}' exceeds the maximum motion sequence length of {AcxFormatConstants.MaxMotionSequenceLength}.");
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

        if (jProfile.InstanceCount < 0 || jProfile.InstanceCount > AcxFormatConstants.MaxJInstanceCount) {
            errors.Add($"J profile has invalid instance count. Maximum is {AcxFormatConstants.MaxJInstanceCount}.");
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

        int maxInstances = cascade.ModuleKind switch
        {
            HeadCascadeModuleKind.Yarn => AcxFormatConstants.MaxYarnInstanceCount,
            HeadCascadeModuleKind.Stitch => AcxFormatConstants.MaxStitchInstanceCount,
            _ => 0,
        };

        if (cascade.InstanceCount > maxInstances) {
            errors.Add($"Cascade profile '{cascade.ModuleKind}' exceeds the maximum instance count of {maxInstances}.");
        }

        int expectedAddressCount = cascade.AddressesPerInstance * cascade.InstanceCount;
        int maxAddressCount = cascade.ModuleKind switch
        {
            HeadCascadeModuleKind.Yarn => AcxFormatConstants.MaxYarnAddresses,
            HeadCascadeModuleKind.Stitch => AcxFormatConstants.MaxStitchAddresses,
            _ => 0,
        };

        if (cascade.AddressCount != expectedAddressCount) {
            errors.Add($"Cascade profile '{cascade.ModuleKind}' address count mismatch.");
        }

        if (cascade.AddressCount > maxAddressCount) {
            errors.Add($"Cascade profile '{cascade.ModuleKind}' exceeds the maximum address count of {maxAddressCount}.");
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
        } else if (stop.Frame is not null) {
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

        if (frame.Dlc > AcxFormatConstants.MaxCanDlc) {
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

    private static void ValidateActions(IReadOnlyList<ProfileActionModel> actions, ICollection<string> errors, ICollection<string> warnings)
    {
        if (actions is null) {
            errors.Add("Actions collection cannot be null.");
            return;
        }

        if (actions.Count > AcxFormatConstants.MaxActions) {
            errors.Add($"Actions count exceeds the maximum of {AcxFormatConstants.MaxActions}.");
        }

        HashSet<string> actionNames = new(StringComparer.OrdinalIgnoreCase);
        int totalSteps = 0;
        for (int i = 0; i < actions.Count; i++) {
            ProfileActionModel action = actions[i];
            if (action is null) {
                errors.Add("Action cannot be null.");
                continue;
            }

            ValidateActionName(action.ActionName, errors);
            ValidateTextLength(action.Category, AcxFormatConstants.MaxMetadataStringBytes, "Action category", errors);
            if (!actionNames.Add(action.ActionName)) {
                errors.Add($"Duplicate action '{action.ActionName}'.");
            }

            totalSteps += action.StepCount;
            if (action.StepCount > AcxFormatConstants.MaxCommands) {
                errors.Add($"Action '{action.ActionName}' exceeds the maximum step count of {AcxFormatConstants.MaxCommands}.");
            }

            ValidateActionSteps(action.Steps, action.ActionName, errors);
        }

        if (totalSteps > AcxFormatConstants.MaxCommands) {
            errors.Add($"The total number of action steps exceeds the maximum of {AcxFormatConstants.MaxCommands}.");
        }

        if (actions.Count == 0) {
            warnings.Add("No dynamic actions are defined.");
        }
    }

    private static void ValidateActionName(string actionName, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(actionName)) {
            errors.Add("Action name cannot be empty.");
            return;
        }

        ValidateTextLength(actionName, AcxFormatConstants.MaxActionNameUtf8Bytes, "Action name", errors);
        if (HasDisallowedNameChars(actionName)) {
            errors.Add($"Action name '{actionName}' contains unsupported filename or protocol characters.");
        }
    }

    private static void ValidateActionSteps(IReadOnlyList<ProfileActionStepModel> steps, string actionName, ICollection<string> errors)
    {
        if (steps is null) {
            errors.Add($"Action '{actionName}' steps cannot be null.");
            return;
        }

        for (int i = 0; i < steps.Count; i++) {
            ProfileActionStepModel step = steps[i];
            if (step is null) {
                errors.Add($"Action '{actionName}' contains a null step.");
                continue;
            }

            if (step.StepOrder != i) {
                errors.Add($"Action '{actionName}' step order is invalid at position {i}.");
            }

            ValidateActionStep(step, actionName, errors);
        }
    }

    private static void ValidateActionStep(ProfileActionStepModel step, string actionName, ICollection<string> errors)
    {
        switch (step.StepKind) {
            case HeadInitStepKind.Can:
                if (step.Bus is not (1 or 2)) {
                    errors.Add($"Action '{actionName}' CAN step has an invalid bus.");
                }

                if (!step.CanId.HasValue || step.CanId.Value > MaxCanId) {
                    errors.Add($"Action '{actionName}' CAN step has invalid CAN id.");
                }

                if (!step.Dlc.HasValue || step.Dlc.Value > AcxFormatConstants.MaxCanDlc) {
                    errors.Add($"Action '{actionName}' CAN step has invalid DLC.");
                }

                if (step.Data is null) {
                    errors.Add($"Action '{actionName}' CAN step is missing data.");
                } else if (step.Dlc.HasValue && step.Data.Length != step.Dlc.Value) {
                    errors.Add($"Action '{actionName}' CAN step has a DLC/data length mismatch.");
                }

                if (step.WaitMs.HasValue) {
                    errors.Add($"Action '{actionName}' CAN step must not set a wait time.");
                }

                break;
            case HeadInitStepKind.Wait:
                if (step.Bus.HasValue || step.CanId.HasValue || step.Dlc.HasValue || step.Data is not null) {
                    errors.Add($"Action '{actionName}' WAIT step has CAN fields set.");
                }

                if (!step.WaitMs.HasValue || step.WaitMs.Value < 0 || step.WaitMs.Value > (int)AcxFormatConstants.MaxWaitMilliseconds) {
                    errors.Add($"Action '{actionName}' WAIT step has an invalid delay.");
                }

                break;
            case HeadInitStepKind.Status:
                if (step.Bus.HasValue || step.CanId.HasValue || step.Dlc.HasValue || step.Data is not null || step.WaitMs.HasValue) {
                    errors.Add($"Action '{actionName}' STATUS step must not contain CAN or wait fields.");
                }

                break;
            default:
                errors.Add($"Action '{actionName}' has an unsupported step kind.");
                break;
        }
    }

    private static void ValidateProfileKey(string profileKey, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(profileKey)) {
            errors.Add("Profile key cannot be empty.");
            return;
        }

        ValidateTextLength(profileKey, AcxFormatConstants.MaxProfileKeyUtf8Bytes, "Profile key", errors);
        if (HasDisallowedNameChars(profileKey)) {
            errors.Add("Profile key contains unsupported filename or protocol characters.");
        }
    }

    private static void ValidateDisplayName(string displayName, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(displayName)) {
            errors.Add("Display name cannot be empty.");
            return;
        }

        ValidateTextLength(displayName, AcxFormatConstants.MaxProfileDisplayNameUtf8Bytes, "Display name", errors);
    }

    private static void ValidateTextLength(string value, int maxBytes, string label, ICollection<string> errors)
    {
        if (value is null) {
            errors.Add($"{label} cannot be null.");
            return;
        }

        if (Utf8.GetByteCount(value) > maxBytes) {
            errors.Add($"{label} exceeds the maximum length of {maxBytes} bytes.");
        }
    }

    private static bool HasDisallowedNameChars(string name)
    {
        return name.Contains("..", StringComparison.Ordinal)
            || name.Contains('/')
            || name.Contains('\\')
            || name.Contains('|')
            || name.Contains('\r')
            || name.Contains('\n');
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
