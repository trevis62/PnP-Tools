using SearchQueryTool.Model;

namespace PSSQT
{
    internal interface IBatchProcessor
    {
        bool NotFinished { get; }

        void Initialize(SearchQueryRequest searchRequest, int searchResultBatchSize);
    }
}