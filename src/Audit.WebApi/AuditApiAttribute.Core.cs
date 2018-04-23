﻿#if NETSTANDARD2_0 || NETSTANDARD1_6 || NET451
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Audit.Core;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc;
using Audit.Core.Extensions;
using Newtonsoft.Json;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Audit.WebApi
{

    public class AuditApiAttribute : ActionFilterAttribute
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
        /// Gets or sets a value indicating whether the output should include the Http Response text.
        /// </summary>
        public bool IncludeResponseBody { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether the Request Body should be read and incuded on the event.
        /// </summary>
        /// <remarks>
        /// When IncludeResquestBody is set to true and you are not using a [FromBody] parameter (i.e.reading the request body directly from the Request)
        /// make sure you enable rewind on the request body stream, otherwise the controller won't be able to read the request body since, by default, 
        /// it's a forwand-only stream that can be read only once. 
        /// </remarks>
        public bool IncludeRequestBody { get; set; }

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

        /// <summary>
        /// Gets or sets a value indicating whether the action arguments should be pre-serialized to the audit event.
        /// </summary>
        public bool SerializeActionParameters { get; set; }

        private const string AuditApiActionKey = "__private_AuditApiAction__";
        private const string AuditApiScopeKey = "__private_AuditApiScope__";

        /// <summary>
        /// Occurs before the action method is invoked.
        /// </summary>
        /// <param name="actionContext">The action context.</param>
        private async Task BeforeExecutingAsync(ActionExecutingContext actionContext)
        {
            var httpContext = actionContext.HttpContext;
            var actionDescriptior = actionContext.ActionDescriptor as ControllerActionDescriptor;
            var auditAction = new AuditApiAction
            {
                UserName = httpContext.User?.Claims.Where(c => c.Type == "preferred_username").Select(c => c.Value).FirstOrDefault() ?? httpContext.User?.Identity.Name,
                IpAddress = httpContext.Connection?.RemoteIpAddress?.ToString(),
                RequestUrl = string.Format("{0}://{1}{2}", httpContext.Request.Scheme, httpContext.Request.Host, httpContext.Request.Path),
                HttpMethod = actionContext.HttpContext.Request.Method,
                FormVariables = httpContext.Request.HasFormContentType ? ToDictionary(httpContext.Request.Form) : null, 
                Headers = IncludeHeaders ? ToDictionary(httpContext.Request.Headers) : null,
                ActionName = actionDescriptior != null ? actionDescriptior.ActionName : actionContext.ActionDescriptor.DisplayName,
                ControllerName = actionDescriptior != null ? actionDescriptior.ControllerName : null,
                ActionParameters = GetActionParameters(actionContext.ActionArguments),
                RequestBody = new BodyContent { Type = httpContext.Request.ContentType, Length = httpContext.Request.ContentLength, Value = IncludeRequestBody ? GetRequestBody(actionContext) : null}
            };
            var eventType = (EventTypeName ?? "{verb} {controller}/{action}").Replace("{verb}", auditAction.HttpMethod)
                .Replace("{controller}", auditAction.ControllerName)
                .Replace("{action}", auditAction.ActionName);
            // Create the audit scope
            var auditEventAction = new AuditEventWebApi()
            {
                Action = auditAction,
                EventId = EventId,
                TenantId = httpContext.User?.Claims.Where(c => c.Type == "tenant").Select(c => c.Value).FirstOrDefault()
            };
            var auditScope = await AuditScope.CreateAsync(new AuditScopeOptions() { Source = Source ?? AuditApiDefaults.Source , EventType = eventType, AuditEvent = auditEventAction, CallingMethod = actionDescriptior.MethodInfo });

            //Use request username or environment username as fall back on the event data
            auditEventAction.UserName = auditAction.UserName ?? auditEventAction.Environment?.UserName;

            httpContext.Items[AuditApiActionKey] = auditAction;
            httpContext.Items[AuditApiScopeKey] = auditScope;
        }

        /// <summary>
        /// Occurs after the action method is invoked.
        /// </summary>
        /// <param name="context">The action executed context.</param>
        private async Task AfterExecutedAsync(ActionExecutedContext context)
        {
            var httpContext = context.HttpContext;
            var auditAction = httpContext.Items[AuditApiActionKey] as AuditApiAction;
            var auditScope = httpContext.Items[AuditApiScopeKey] as AuditScope;
            if (auditAction != null && auditScope != null)
            {
                auditAction.Exception = context.Exception.GetExceptionInfo();
                auditAction.ModelStateErrors = IncludeModelState ? AuditApiHelper.GetModelStateErrors(context.ModelState) : null;
                auditAction.ModelStateValid = IncludeModelState ? context.ModelState?.IsValid : null;
                if (context.HttpContext.Response != null && context.Result != null)
                {
                    var statusCode = context.Result is ObjectResult && (context.Result as ObjectResult).StatusCode.HasValue ? (context.Result as ObjectResult).StatusCode.Value  
                        : context.Result is StatusCodeResult ? (context.Result as StatusCodeResult).StatusCode : context.HttpContext.Response.StatusCode;

                    if(statusCode >= 100 && statusCode <= 399) //Informational responses (100-199), redirects (300-399) and success codes (200-299)
                    {
                        auditScope.Event.AuditLevel = AuditEvent.AuditLevels.Success;
                    }
                    else if(statusCode >= 400 && statusCode <= 499)//Bad requests and conflicts etc anything in the 400-499 range
                    {
                        auditScope.Event.AuditLevel = AuditEvent.AuditLevels.Failure;
                    }
                    else //500-599 range
                    {
                        auditScope.Event.AuditLevel = AuditEvent.AuditLevels.Error;
                    }

                    auditAction.ResponseStatusCode = statusCode;
                    auditAction.ResponseStatus = GetStatusCodeString(auditAction.ResponseStatusCode);
                    if (IncludeResponseBody)
                    {
                        var bodyType = context.Result?.GetType().GetFullTypeName();
                        if (bodyType != null)
                        {
                            auditAction.ResponseBody = new BodyContent { Type = bodyType, Value = GetResponseBody(context.Result) };
                        }
                    }
                }
                else
                {
                    auditScope.Event.AuditLevel = AuditEvent.AuditLevels.Error;
                    auditAction.ResponseStatusCode = 500;
                    auditAction.ResponseStatus = "Internal Server Error";
                }
                // Replace the Action field and save
                (auditScope.Event as AuditEventWebApi).Action = auditAction;
                await auditScope.SaveAsync();
            }
        }

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            await BeforeExecutingAsync(context);
            var actionExecutedContext = await next.Invoke();
            await AfterExecutedAsync(actionExecutedContext);
        }

        private object GetResponseBody(IActionResult result)
        {
            if (result is ObjectResult or)
            {
                return or.Value;
            }
            if (result is StatusCodeResult sr)
            {
                return sr.StatusCode;
            }
            if (result is JsonResult jr)
            {
                return jr.Value;
            }
            if (result is ContentResult cr)
            {
                return cr.Content;
            }
            if (result is FileResult fr)
            {
                return fr.FileDownloadName;
            }
            if (result is LocalRedirectResult lrr)
            {
                return lrr.Url;
            }
            if (result is RedirectResult rr)
            {
                return rr.Url;
            }
            if (result is RedirectToActionResult rta)
            {
                return rta.ActionName;
            }
            if (result is RedirectToRouteResult rtr)
            {
                return rtr.RouteName;
            }
            if (result is SignInResult sir)
            {
                return sir.Principal?.Identity?.Name;
            }
            if (result is PartialViewResult pvr)
            {
                return pvr.ViewName;
            }
            if (result is ViewComponentResult vc)
            {
                return vc.ViewComponentName;
            }
            if (result is ViewResult vr)
            {
                return vr.ViewName;
            }
#if NETSTANDARD2_0
            if (result is RedirectToPageResult rtp)
            {
                return rtp.PageName;
            }
#endif
            return result.ToString();
        }

        private string GetStatusCodeString(int statusCode)
        {
            var name = ((HttpStatusCode)statusCode).ToString();
            string[] words = Regex.Matches(name, "(^[a-z]+|[A-Z]+(?![a-z])|[A-Z][a-z]+)")
                .OfType<Match>()
                .Select(m => m.Value)
                .ToArray();
            return words.Length == 0 ? name : string.Join(" ", words);
        }

        private IDictionary<string, object> GetActionParameters(IDictionary<string, object> actionArguments)
        {
            if (SerializeActionParameters)
            {
                return AuditApiHelper.SerializeParameters(actionArguments);
            }
            return actionArguments;
        }

        private static IDictionary<string, string> ToDictionary(IEnumerable<KeyValuePair<string, StringValues>> col)
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

        internal static AuditScope GetCurrentScope(HttpContext httpContext)
        {
            return httpContext.Items[AuditApiScopeKey] as AuditScope;
        }

        private string GetRequestBody(ActionExecutingContext actionContext)
        {
            var body = actionContext.HttpContext.Request.Body;
            if (body != null && body.CanRead)
            {
                using (var stream = new MemoryStream())
                {
                    if (body.CanSeek)
                    {
                        body.Seek(0, SeekOrigin.Begin);
                    }
                    body.CopyTo(stream);
                    if (body.CanSeek)
                    {
                        body.Seek(0, SeekOrigin.Begin);
                    }
                    return Encoding.UTF8.GetString(stream.ToArray());
                }
            }
            return null;
        }
    }
}
#endif