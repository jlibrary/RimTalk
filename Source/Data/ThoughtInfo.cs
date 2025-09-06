namespace RimTalk.Data
{
    public class ThoughtInfo
    {
        public string DefName { get; }
        public string Label { get; }
        public float MoodOffset { get; }
        public float DurationDays { get; }

        public ThoughtInfo(string defName, string label, float moodOffset, float durationDays)
        {
            DefName = defName;
            Label = label;
            MoodOffset = moodOffset;
            DurationDays = durationDays;
        }
    }
}