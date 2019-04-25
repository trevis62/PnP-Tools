using PSSQT.Helpers;
using SearchQueryTool.Model;
using System.Management.Automation;
using System.Web.Script.Serialization;

namespace PSSQT
{
    public class UnexpectedResultException : RuntimeException
    {
        public UnexpectedResultException(SearchQueryResult result, string message = null) : base($"Unexpected result! {message}")
        {
            var serializer = new JavaScriptSerializer();

            // Request
            var hdrs = result.RequestHeaders.ToDictionary();

            Data.Add("RequestHeaders", serializer.Serialize(hdrs));
            Data.Add("RequestContent", result.RequestContent);

            // Response
            hdrs = result.ResponseHeaders.ToDictionary();

            Data.Add("ResponseHeaders", serializer.Serialize(hdrs));
            Data.Add("ResponseContent", result.ResponseContent);
        }
    }
}
