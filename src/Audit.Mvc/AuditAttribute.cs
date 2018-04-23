#if NET45
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Audit.Core;
using Audit.Core.Extensions;
using System.Security.Claims;

namespace Audit.Mvc
{
    /// <summary>
    /// Action Filter to Audit an Mvc Action
    /// </summary>
    public class AuditAttribute : ActionFilterAttribute
    {
        /// <summary>
        /// Gets or sets a value indicating whether the output should include the serialized model.
        /// </summary>
        public bool IncludeModel { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether the output should include the Http Request Headers.
        /// </summary>
        public bool IncludeHeaders { get; set; }
        /// <summary>
        /// Gets or sets a value indicating the source of the event
        /// </summary>
        /// <remarks>Overrides the globally defined event source</remarks>
        public string Source { get; set; }
        /// <summary>
        /// Gets or sets a value indicating the event type name
        /// Can contain the following placeholders:
        /// - {controller}: replaced with the controller name.
        /// - {action}: replaced with the action method name.
        /// - {verb}: replaced with the HTTP verb used (GET, POST, etc).
        /// </summary>
        public string EventTypeName { get; set; }
        /// <summary>
        /// Gets or sets the id number associated with this type of event
        /// </summary>
        /// <remarks>This an id that describes the type of even and should not be confused with the id of the AuditEvent written to the data provider</remarks>
        public int? EventId { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether the action arguments should be pre-serialized to the audit event.
        /// </summary>
        public bool SerializeActionParameters { get; set; }

        private const string AuditActionKey = "__private_AuditAction__";
        private const string AuditScopeKey = "__private_AuditScope__";

        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            var request = filterContext.HttpContext.Request;
            var claimsIdentity = ClaimsPrincipal.Current?.Identity as ClaimsIdentity;

            var auditAction = new AuditAction()
            {
                UserName = (request.IsAuthenticated) ? filterContext.HttpContext.User?.Identity.Name : "Anonymous",
                IpAddress = request.ServerVariables?["HTTP_X_FORWARDED_FOR"] ?? request.UserHostAddress,
#if NET45
                RequestUrl = request.Unvalidated.RawUrl,
                FormVariables = ToDictionary(request.Unvalidated.Form),
                Headers = IncludeHeaders ? ToDictionary(request.Unvalidated.Headers) : null,
#else
                RequestUrl = request.RawUrl,
                FormVariables = ToDictionary(request.Form),
                Headers = IncludeHeaders ? ToDictionary(request.Headers) : null,
#endif
                HttpMethod = request.HttpMethod,
                ActionName = filterContext.ActionDescriptor?.ActionName,
                ControllerName = filterContext.ActionDescriptor?.ControllerDescriptor?.ControllerName,
                ActionParameters = GetActionParameters(filterContext.ActionParameters)
            };
            var eventType = (EventTypeName ?? "{verb} {controller}/{action}").Replace("{verb}", auditAction.HttpMethod)
                .Replace("{controller}", auditAction.ControllerName)
                .Replace("{action}", auditAction.ActionName);
            // Create the audit scope
            var auditEventAction = new AuditEventMvcAction()
            {
                Action = auditAction,
                EventId = EventId,
                TenantId = claimsIdentity?.Claims.Where(c => c.Type == "tenant").Select(c => c.Value).FirstOrDefault()
            };
            var options = new AuditScopeOptions()
            {
                Source = Source ?? AuditDefaults.Source,
                EventType = eventType,
                AuditEvent = auditEventAction,
                CallingMethod = (filterContext.ActionDescriptor as ReflectedActionDescriptor)?.MethodInfo
            };
            var auditScope = AuditScope.Create(options);
            filterContext.HttpContext.Items[AuditActionKey] = auditAction;
            filterContext.HttpContext.Items[AuditScopeKey] = auditScope;
            base.OnActionExecuting(filterContext);
        }

        public override void OnActionExecuted(ActionExecutedContext filterContext)
        {
            var auditAction = filterContext.HttpContext.Items[AuditActionKey] as AuditAction;
            if (auditAction != null)
            {
                auditAction.ModelStateErrors = IncludeModel ? AuditHelper.GetModelStateErrors(filterContext.Controller?.ViewData.ModelState) : null;
                auditAction.Model = IncludeModel ? filterContext.Controller?.ViewData.Model : null;
                auditAction.ModelStateValid = IncludeModel ? filterContext.Controller?.ViewData.ModelState.IsValid : null;
                auditAction.Exception = filterContext.Exception.GetExceptionInfo();
            }
            var auditScope = filterContext.HttpContext.Items[AuditScopeKey] as AuditScope;
            if (auditScope != null)
            {
                if(auditAction?.Exception != null)
                {
                    auditScope.Event.AuditLevel = AuditEvent.AuditLevels.Error;
                }

                // Replace the Action field
                (auditScope.Event as AuditEventMvcAction).Action = auditAction;
            }
            base.OnActionExecuted(filterContext);
        }

        public override void OnResultExecuted(ResultExecutedContext filterContext)
        {
            var auditAction = filterContext.HttpContext.Items[AuditActionKey] as AuditAction;
            if (auditAction != null)
            {
                var viewResult = filterContext.Result as ViewResult;
                var razorView = viewResult?.View as RazorView;
                auditAction.ViewName = viewResult?.ViewName;
                auditAction.ViewPath = razorView?.ViewPath;
                auditAction.RedirectLocation = filterContext.HttpContext.Response.RedirectLocation;
                auditAction.ResponseStatus = filterContext.HttpContext.Response.Status;
                auditAction.ResponseStatusCode = filterContext.HttpContext.Response.StatusCode;
                auditAction.Exception = filterContext.Exception.GetExceptionInfo();
            }
            var auditScope = filterContext.HttpContext.Items[AuditScopeKey] as AuditScope;
            if (auditScope != null)
            {
                if (auditAction.ResponseStatusCode >= 100 && auditAction.ResponseStatusCode <= 399) //Informational responses (100-199), redirects (300-399) and success codes (200-299)
                {
                    auditScope.Event.AuditLevel = AuditEvent.AuditLevels.Success;
                }
                else if (auditAction.ResponseStatusCode >= 400 && auditAction.ResponseStatusCode <= 499)//Bad requests and conflicts etc anything in the 400-499 range
                {
                    auditScope.Event.AuditLevel = AuditEvent.AuditLevels.Failure;
                }
                else //500-599 range
                {
                    auditScope.Event.AuditLevel = AuditEvent.AuditLevels.Error;
                }

                // Replace the Action field 
                (auditScope.Event as AuditEventMvcAction).Action = auditAction;
                if (auditScope.EventCreationPolicy == EventCreationPolicy.Manual)
                {
                    auditScope.Save(); // for backwards compatibility
                }
                auditScope.Dispose();
            }
            base.OnResultExecuted(filterContext);
        }

        private IDictionary<string, object> GetActionParameters(IDictionary<string, object> actionArguments)
        {
            if (SerializeActionParameters)
            {
                return AuditHelper.SerializeParameters(actionArguments);
            }
            return actionArguments.ToDictionary(k => k.Key, v => v.Value);
        }

        private static IDictionary<string, string> ToDictionary(NameValueCollection col)
        {
            if (col == null)
            {
                return null;
            }
            IDictionary<string, string> dict = new Dictionary<string, string>();
            foreach (var k in col.AllKeys)
            {
                dict.Add(k, col[k]);
            }
            return dict;
        }

        internal static AuditScope GetCurrentScope(HttpContextBase httpContext)
        {
            return httpContext?.Items[AuditScopeKey] as AuditScope;
        }
    }
}
#endif