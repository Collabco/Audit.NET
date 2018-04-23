﻿#if NET45
using Audit.Core;
using Audit.Core.Extensions;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using System.Web.Http.ModelBinding;

namespace Audit.WebApi
{
    public class AuditApiAttribute : System.Web.Http.Filters.ActionFilterAttribute
    {
        /// <summary>
        /// Gets or sets a value indicating whether the output should include the Http Request Headers.
        /// </summary>
        public bool IncludeHeaders { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether the output should include Model State information.
        /// </summary>
        public bool IncludeModelState { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether the output should include the Http Response body.
        /// </summary>
        public bool IncludeResponseBody { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether the output should include the Http request body string.
        /// </summary>
        public bool IncludeRequestBody { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether the action arguments should be pre-serialized to the audit event.
        /// </summary>
        public bool SerializeActionParameters { get; set; }
        /// <summary>
        /// Gets or sets a value indicating the source of the event
        /// </summary>
        /// <remarks>Overrides the globally defined event source</remarks>
        public string Source { get; set; }
        /// <summary>
        /// Gets or sets a string indicating the event type to use.
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

        private const string AuditApiActionKey = "__private_AuditApiAction__";
        private const string AuditApiScopeKey = "__private_AuditApiScope__";
        
        /// <summary>
        /// Occurs before the action method is invoked.
        /// </summary>
        /// <param name="actionContext">The action context.</param>
        private async Task BeforeExecutingAsync(HttpActionContext actionContext)
        {
            var request = actionContext.Request;
            var contextWrapper = new ContextWrapper(request);

            var claimsIdentity = ClaimsPrincipal.Current?.Identity as ClaimsIdentity;

            var auditAction = new AuditApiAction
            {
                UserName = claimsIdentity?.Claims.Where(c => c.Type == "preferred_username").Select(c => c.Value).FirstOrDefault() ?? actionContext.RequestContext?.Principal?.Identity?.Name,
                IpAddress = contextWrapper.GetClientIp(),
                RequestUrl = request.RequestUri?.AbsoluteUri,
                HttpMethod = actionContext.Request.Method?.Method,
                FormVariables = contextWrapper.GetFormVariables(),
                Headers = IncludeHeaders ? ToDictionary(request.Headers) : null,
                ActionName = actionContext.ActionDescriptor?.ActionName,
                ControllerName = actionContext.ActionDescriptor?.ControllerDescriptor?.ControllerName,
                ActionParameters = GetActionParameters(actionContext.ActionArguments),
                RequestBody = IncludeRequestBody ? GetRequestBody(contextWrapper) : null
            };
            var eventType = (EventTypeName ?? "{verb} {controller}/{action}").Replace("{verb}", auditAction.HttpMethod)
                .Replace("{controller}", auditAction.ControllerName)
                .Replace("{action}", auditAction.ActionName);
            // Create the audit scope
            var auditEventAction = new AuditEventWebApi()
            {
                Action = auditAction,
                EventId = EventId,
                TenantId = claimsIdentity?.Claims.Where(c => c.Type == "tenant").Select(c => c.Value).FirstOrDefault()
            };
            var options = new AuditScopeOptions()
            {
                Source = Source ?? AuditApiDefaults.Source,
                EventType = eventType,
                AuditEvent = auditEventAction,
                CallingMethod = (actionContext.ActionDescriptor as ReflectedHttpActionDescriptor)?.MethodInfo
            };
            var auditScope = await AuditScope.CreateAsync(options);
            contextWrapper.Set(AuditApiActionKey, auditAction);
            contextWrapper.Set(AuditApiScopeKey, auditScope);
        }

        /// <summary>
        /// Occurs after the action method is invoked.
        /// </summary>
        /// <param name="actionExecutedContext">The action executed context.</param>
        private async Task AfterExecutedAsync(HttpActionExecutedContext actionExecutedContext)
        {
            var contextWrapper = new ContextWrapper(actionExecutedContext.Request);
            var auditAction = contextWrapper.Get<AuditApiAction>(AuditApiActionKey);
            var auditScope = contextWrapper.Get<AuditScope>(AuditApiScopeKey);
            if (auditAction != null && auditScope != null)
            {
                auditAction.Exception = actionExecutedContext.Exception.GetExceptionInfo();
                auditAction.ModelStateErrors = IncludeModelState ? AuditApiHelper.GetModelStateErrors(actionExecutedContext.ActionContext.ModelState) : null;
                auditAction.ModelStateValid = IncludeModelState ? actionExecutedContext.ActionContext.ModelState?.IsValid : null;
                if (actionExecutedContext.Response != null)
                {
                    auditAction.ResponseStatus = actionExecutedContext.Response.ReasonPhrase;
                    auditAction.ResponseStatusCode = (int)actionExecutedContext.Response.StatusCode;

                    if(auditAction.ResponseStatusCode >= 100 && auditAction.ResponseStatusCode <= 399) //Informational responses (100-199), redirects (300-399) and success codes (200-299)
                    {
                        auditScope.Event.AuditLevel = AuditEvent.AuditLevels.Success;
                    }
                    else if(auditAction.ResponseStatusCode >= 400 && auditAction.ResponseStatusCode <= 499)//Bad requests and conflicts etc anything in the 400-499 range
                    {
                        auditScope.Event.AuditLevel = AuditEvent.AuditLevels.Failure;
                    }
                    else //500-599 range
                    {
                        auditScope.Event.AuditLevel = AuditEvent.AuditLevels.Error;
                    }

                    if (IncludeResponseBody)
                    {
                        var objContent = actionExecutedContext.Response.Content as ObjectContent;
                        auditAction.ResponseBody = new BodyContent
                        {
                            Type = objContent != null ? objContent.ObjectType.Name : actionExecutedContext.Response.Content?.Headers?.ContentType.ToString(),
                            Length = actionExecutedContext.Response.Content?.Headers.ContentLength,
                            Value = objContent != null ? objContent.Value : actionExecutedContext.Response.Content?.ReadAsStringAsync().Result
                        };
                    }
                }
                else
                {
                    auditAction.ResponseStatusCode = 500;
                    auditAction.ResponseStatus = "Internal Server Error";
                }
                // Replace the Action field and save
                (auditScope.Event as AuditEventWebApi).Action = auditAction;
                await auditScope.SaveAsync();
            }
        }

        public override async Task OnActionExecutingAsync(HttpActionContext actionContext, CancellationToken cancellationToken)
        {
            await BeforeExecutingAsync(actionContext);
            await base.OnActionExecutingAsync(actionContext, cancellationToken); 
        }

        public override async Task OnActionExecutedAsync(HttpActionExecutedContext actionExecutedContext, CancellationToken cancellationToken)
        {
            await base.OnActionExecutedAsync(actionExecutedContext, cancellationToken);
            await AfterExecutedAsync(actionExecutedContext);
        }

        private BodyContent GetRequestBody(ContextWrapper contextWrapper)
        {
            var context = contextWrapper.GetHttpContext();
            if (context?.Request?.InputStream != null)
            {
                using (var stream = new MemoryStream())
                {
                    context.Request.InputStream.Seek(0, SeekOrigin.Begin);
                    context.Request.InputStream.CopyTo(stream);
                    var body = Encoding.UTF8.GetString(stream.ToArray());
                    return new BodyContent
                    {
                        Type = context.Request.ContentType,
                        Length = context.Request.ContentLength,
                        Value = body
                    };
                }
            }
            return null;
        }

        private IDictionary<string, object> GetActionParameters(IDictionary<string, object> actionArguments)
        {
            if (SerializeActionParameters)
            {
                return AuditApiHelper.SerializeParameters(actionArguments);
            }
            return actionArguments;
        }

        private static IDictionary<string, string> ToDictionary(HttpRequestHeaders col)
        {
            if (col == null)
            {
                return null;
            }
            IDictionary<string, string> dict = new Dictionary<string, string>();
            foreach (var k in col)
            {
                dict.Add(k.Key, string.Join(", ", k.Value));
            }
            return dict;
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


        internal static AuditScope GetCurrentScope(HttpRequestMessage request)
        {
            var contextWrapper = new ContextWrapper(request);
            return contextWrapper.Get<AuditScope>(AuditApiScopeKey);
        }
    }
}
#endif