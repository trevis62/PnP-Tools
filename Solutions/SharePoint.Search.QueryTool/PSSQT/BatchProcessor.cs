using SearchQueryTool.Model;
using System;

namespace PSSQT
{
    internal class BatchProcessor : IBatchProcessor
    {
        public bool NotFinished => throw new NotImplementedException();

        internal static IBatchProcessor Create()
        {
            return new BatchProcessor();
        }

        public void Initialize(SearchQueryRequest searchRequest, int searchResultBatchSize)
        {
            throw new NotImplementedException();
        }
    }
}