﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace GrobExp.Mutators.MutatorsRecording.AssignRecording
{
    public class AssignRecordCollection
    {
        public AssignRecordCollection()
        {
            converterRecords = new List<RecordNode>();
        }

        public void AddConverterToRecord(string converterName)
        {
            currentConverterRecord = converterRecords.FirstOrDefault(converter => converter.Name == converterName);
            if (currentConverterRecord != null) return;
            currentConverterRecord = new RecordNode(converterName, "");
            converterRecords.Add(currentConverterRecord);
        }

        public void ResetCurrentConvertor()
        {
            currentConverterRecord = null;
        }

        public void RecordCompilingExpression(string path, string value, bool isExcludedFromCoverage = false)
        {
            if (currentConverterRecord != null)
                currentConverterRecord.RecordCompilingExpression(path.Split('.').ToList(), value, isExcludedFromCoverage);
        }

        public void RecordExecutingExpression(string path, string value, Lazy<bool> isExcludedFromCoverage = null)
        {
            if (currentConverterRecord != null)
                currentConverterRecord.RecordExecutingExpression(path.Split('.').ToList(), value, isExcludedFromCoverage);
        }

        public List<RecordNode> GetRecords()
        {
            return converterRecords;
        }

        private readonly List<RecordNode> converterRecords;
        private RecordNode currentConverterRecord;
    }
}