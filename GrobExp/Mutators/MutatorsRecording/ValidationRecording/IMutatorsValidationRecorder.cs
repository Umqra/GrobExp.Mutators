﻿using System.Collections.Generic;

namespace GrobExp.Mutators.MutatorsRecording.ValidationRecording
{
    public interface IMutatorsValidationRecorder
    {
        List<RecordNode> GetRecords();
        List<RecordNode> GetErrorRecords();
        void Stop();
    }
}