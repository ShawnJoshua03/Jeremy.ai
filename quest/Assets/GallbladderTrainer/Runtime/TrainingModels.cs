using System;

namespace GallbladderTrainer
{
    /// <summary>Stable structure IDs. Do not rename: they are part of the backend hook contract.</summary>
    public static class StructureIds
    {
        public const string Gallbladder = "gallbladder";
        public const string CysticDuct = "cystic_duct";
        public const string CysticArtery = "cystic_artery";
        public const string CysticPlate = "cystic_plate";
    }

    /// <summary>Stable decision IDs. Identify-type submissions use an empty DecisionId and set SelectedStructureId.</summary>
    public static class DecisionIds
    {
        public const string CriteriaDemonstrated = "criteria_demonstrated";
        public const string CriteriaNotDemonstrated = "criteria_not_demonstrated";
        public const string RequestInstructor = "request_instructor";
    }

    public enum TrainingMode { Explore, Assess, Review }
    public enum ExerciseKind { Identify, Cvs }
    public enum ResultStatus { None, Match, Mismatch, Unclear }
    public enum CommandSource { LocalDemo, Remote }

    public sealed class Exercise
    {
        public string Id;
        public string Title;        // neutral title shown in Explore/Assess
        public string ReviewName;   // revealed in Review
        public string Task;
        public ExerciseKind Kind;
        public string TargetStructureId;   // Identify
        public string ExpectedDecisionId;  // Cvs
        public bool LowerThirdSeparated;   // prepared visibility state (not simulated dissection)
        public bool[] CriteriaShown;       // 3 entries, prepared teaching state
        public string InstructorFocusId;
        public string InstructorNote;
        public string FeedbackMatch;
        public string FeedbackMismatch;
    }

    public static class ExerciseLibrary
    {
        public const string Disclaimer = "Educational prototype\u2014not a validated assessment of surgical competence.";
        public const string SchematicNote = "Schematic, approximated anatomy. Prepared teaching states are not simulated dissection or clinical ground truth.";
        public const string LabelDemonstrated = "Criteria demonstrated in this prepared exercise";
        public const string LabelNotDemonstrated = "Criteria not demonstrated";
        public const string LabelUnclear = "Unclear; request instructor review";

        public static readonly string[] CriteriaText =
        {
            "Hepatocystic triangle cleared of fat and fibrous tissue",
            "Lower third of the gallbladder separated from the liver to expose the cystic plate",
            "Two, and only two, structures seen entering the gallbladder"
        };

        public static readonly Exercise[] All =
        {
            new Exercise
            {
                Id = "ex_identify", Title = "1. Anatomy identification", ReviewName = "1. Anatomy identification",
                Kind = ExerciseKind.Identify, TargetStructureId = StructureIds.CysticDuct,
                Task = "Select the CYSTIC DUCT: the narrow tube that leaves the neck of the gallbladder. Use the controller ray and trigger, then press Submit.",
                LowerThirdSeparated = true, CriteriaShown = new[] { true, true, true },
                InstructorFocusId = StructureIds.Gallbladder,
                InstructorNote = "Look at the gallbladder neck, then follow the narrow tube leaving it.",
                FeedbackMatch = "The cystic duct leaves the gallbladder neck and joins the larger common hepatic duct.",
                FeedbackMismatch = "Follow the narrow tube from the gallbladder neck toward the larger common hepatic duct, then try again."
            },
            new Exercise
            {
                Id = "ex_view_a", Title = "2. Prepared view A", ReviewName = "2. Missing cystic-plate exposure",
                Kind = ExerciseKind.Cvs, ExpectedDecisionId = DecisionIds.CriteriaNotDemonstrated,
                Task = "Inspect this prepared view of the gallbladder, liver and hepatocystic triangle. Are the three CVS criteria demonstrated in this prepared exercise?",
                LowerThirdSeparated = false, CriteriaShown = new[] { true, false, true },
                InstructorFocusId = StructureIds.Gallbladder,
                InstructorNote = "Look at where the lower third of the gallbladder meets the liver.",
                FeedbackMatch = "The lower third still lies on the liver, so the cystic plate is not exposed (criterion 2).",
                FeedbackMismatch = "Criterion 2 is missing here: the lower third has not been separated from the liver, so the cystic plate is not exposed."
            },
            new Exercise
            {
                Id = "ex_view_b", Title = "3. Prepared view B", ReviewName = "3. Complete schematic teaching view",
                Kind = ExerciseKind.Cvs, ExpectedDecisionId = DecisionIds.CriteriaDemonstrated,
                Task = "Inspect this second prepared view. Are the three CVS criteria demonstrated in this prepared exercise?",
                LowerThirdSeparated = true, CriteriaShown = new[] { true, true, true },
                InstructorFocusId = StructureIds.CysticArtery,
                InstructorNote = "Count the structures that enter the gallbladder neck.",
                FeedbackMatch = "The triangle is cleared, the lower third is separated to expose the cystic plate, and two structures enter the gallbladder.",
                FeedbackMismatch = "All three criteria are shown in this prepared view. Switch to Review to see each one."
            }
        };

        public static string DecisionLabel(string id)
        {
            switch (id)
            {
                case DecisionIds.CriteriaDemonstrated: return LabelDemonstrated;
                case DecisionIds.CriteriaNotDemonstrated: return LabelNotDemonstrated;
                case DecisionIds.RequestInstructor: return LabelUnclear;
                default: return string.Empty;
            }
        }
    }

    public sealed class DecisionEvent
    {
        public string ExerciseId;
        public string DecisionId;          // empty for Identify submissions
        public string SelectedStructureId; // may be null
        public TrainingMode Mode;
        public ResultStatus Result;
        public DateTime TimestampUtc;
    }

    public struct InstructorCommand
    {
        public CommandSource Source;
        public string StructureId;
        public string Note;
        public bool Clear;
    }

    public struct ExternalFeedback
    {
        public string Text;
        public string SourceLabel;
    }

    /// <summary>Optional receiver for a future backend. Assign a MonoBehaviour implementing this on TrainingController.</summary>
    public interface ITrainingBackendHook
    {
        void OnDecisionSubmitted(DecisionEvent e);
        void OnStructureSelected(string structureId);
    }

    /// <summary>
    /// Local event hub. A future backend can raise InstructorCommandReceived / ExternalFeedbackReceived
    /// (Source = Remote) and listen to the outbound events. No networking is implemented here.
    /// </summary>
    public static class TrainingEvents
    {
        public static event Action<DecisionEvent> DecisionSubmitted;
        public static event Action<string> StructureSelected;
        public static event Action<TrainingMode> ModeChanged;
        public static event Action<InstructorCommand> InstructorCommandReceived;
        public static event Action<ExternalFeedback> ExternalFeedbackReceived;
        public static event Action<string> ExerciseChanged;          // exercise id, raised on every exercise switch
        public static event Action<string, bool> StructureHovered;   // structure id, true = hover start, false = hover end

        public static void RaiseDecision(DecisionEvent e) { if (DecisionSubmitted != null) DecisionSubmitted(e); }
        public static void RaiseExerciseChanged(string id) { if (ExerciseChanged != null) ExerciseChanged(id); }
        public static void RaiseStructureHovered(string id, bool entered) { if (StructureHovered != null) StructureHovered(id, entered); }
        public static void RaiseStructureSelected(string id) { if (StructureSelected != null) StructureSelected(id); }
        public static void RaiseModeChanged(TrainingMode m) { if (ModeChanged != null) ModeChanged(m); }
        public static void SendInstructorCommand(InstructorCommand c) { if (InstructorCommandReceived != null) InstructorCommandReceived(c); }
        public static void SendExternalFeedback(ExternalFeedback f) { if (ExternalFeedbackReceived != null) ExternalFeedbackReceived(f); }
    }
}
