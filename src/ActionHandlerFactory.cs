﻿using BeetleX.FastHttpApi.Data;
using BeetleX.FastHttpApi.WebSockets;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace BeetleX.FastHttpApi
{
    public class ActionHandlerFactory
    {
        static ActionHandlerFactory()
        {

        }

        private System.Collections.Generic.Dictionary<string, ActionHandler> mMethods = new Dictionary<string, ActionHandler>();

        public void Register(HttpConfig config, HttpApiServer server, params Assembly[] assemblies)
        {
            foreach (Assembly item in assemblies)
            {
                Type[] types = item.GetTypes();
                foreach (Type type in types)
                {
                    ControllerAttribute ca = type.GetCustomAttribute<ControllerAttribute>(false);
                    if (ca != null)
                    {
                        try
                        {
                            EventControllerInstanceArgs e = new EventControllerInstanceArgs();
                            e.Type = type;
                            OnControllerInstance(e);
                            if (e.Controller == null)
                            {
                                Register(config, type, Activator.CreateInstance(type), ca.BaseUrl, server, ca);
                            }
                            else
                            {
                                Register(config, type, e.Controller, ca.BaseUrl, server, ca);
                            }
                        }
                        catch (Exception e_)
                        {
                            if (server.EnableLog(EventArgs.LogType.Error))
                            {
                                string msg = $"{type} controller register error {e_.Message} {e_.StackTrace}";
                                server.Log(EventArgs.LogType.Error, msg);
                            }
                        }
                    }
                }
            }
        }

        public object GetController(Type type)
        {
            EventControllerInstanceArgs e = new EventControllerInstanceArgs();
            e.Type = type;
            OnControllerInstance(e);
            return e.Controller;
        }

        protected virtual void OnControllerInstance(EventControllerInstanceArgs e)
        {
            ControllerInstance?.Invoke(this, e);
        }

        public event System.EventHandler<EventControllerInstanceArgs> ControllerInstance;

        public ICollection<ActionHandler> Handlers
        {
            get
            {
                return mMethods.Values;

            }
        }

        public void Register(HttpConfig config, HttpApiServer server, object controller)
        {
            Type type = controller.GetType();
            ControllerAttribute ca = type.GetCustomAttribute<ControllerAttribute>(false);
            if (ca != null)
            {
                Register(config, type, controller, ca.BaseUrl, server, ca);
            }
        }


        public static void RemoveFilter(List<FilterAttribute> filters, Type[] types)
        {
            List<FilterAttribute> removeItems = new List<FilterAttribute>();
            filters.ForEach(a =>
            {
                foreach (Type t in types)
                {
                    if (a.GetType() == t)
                    {
                        removeItems.Add(a);
                        break;
                    }
                }
            });
            foreach (FilterAttribute item in removeItems)
                filters.Remove(item);
        }


        private void Register(HttpConfig config, Type controllerType, object controller, string rooturl, HttpApiServer server, ControllerAttribute ca)
        {
            DataConvertAttribute controllerDataConvert = controllerType.GetCustomAttribute<DataConvertAttribute>(false);
            if (string.IsNullOrEmpty(rooturl))
                rooturl = "/";
            else
            {
                if (rooturl[0] != '/')
                    rooturl = "/" + rooturl;
                if (rooturl[rooturl.Length - 1] != '/')
                    rooturl += "/";
            }
            List<FilterAttribute> filters = new List<FilterAttribute>();
            filters.AddRange(config.Filters);
            IEnumerable<FilterAttribute> fas = controllerType.GetCustomAttributes<FilterAttribute>(false);
            filters.AddRange(fas);
            IEnumerable<SkipFilterAttribute> skipfilters = controllerType.GetCustomAttributes<SkipFilterAttribute>(false);
            foreach (SkipFilterAttribute item in skipfilters)
            {
                RemoveFilter(filters, item.Types);
            }
            object obj = controller;
            if (obj is IController)
            {
                ((IController)obj).Init(server);
            }
            foreach (MethodInfo mi in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (string.Compare("Equals", mi.Name, true) == 0
                    || string.Compare("GetHashCode", mi.Name, true) == 0
                    || string.Compare("GetType", mi.Name, true) == 0
                    || string.Compare("ToString", mi.Name, true) == 0 || mi.Name.IndexOf("set_") >= 0
                    || mi.Name.IndexOf("get_") >= 0)
                    continue;
                if (mi.GetCustomAttribute<NotActionAttribute>(false) != null)
                    continue;
                bool noconvert = false;
                DataConvertAttribute actionConvert = mi.GetCustomAttribute<DataConvertAttribute>();
                if (mi.GetCustomAttribute<NoDataConvertAttribute>(false) != null)
                {
                    noconvert = true;
                    actionConvert = null;
                }
                else
                {
                    if (actionConvert == null)
                        actionConvert = controllerDataConvert;
                }
                string sourceUrl = rooturl + mi.Name;
                string url = sourceUrl;
                string method = HttpParse.GET_TAG;
                string route = null;
                GetAttribute get = mi.GetCustomAttribute<GetAttribute>(false);
                if (get != null)
                {
                    method = HttpParse.GET_TAG;
                    route = get.Route;
                }
                PostAttribute post = mi.GetCustomAttribute<PostAttribute>(false);
                if (post != null)
                {
                    method = HttpParse.POST_TAG;
                    route = post.Route;
                }
                DelAttribute del = mi.GetCustomAttribute<DelAttribute>(false);
                if (del != null)
                {
                    method = HttpParse.DELETE_TAG;
                    route = del.Route;
                }
                PutAttribute put = mi.GetCustomAttribute<PutAttribute>(false);
                if (put != null)
                {
                    method = HttpParse.PUT_TAG;
                    route = put.Route;
                }

                if (server.ServerConfig.UrlIgnoreCase)
                {
                    url = sourceUrl.ToLower();
                }
                RouteTemplateAttribute ra = null;
                if (!string.IsNullOrEmpty(route))
                {
                    ra = new RouteTemplateAttribute(route);
                    string reurl = ra.Analysis(url);
                    if (reurl != null)
                        server.UrlRewrite.Add(reurl, url);
                }
                ActionHandler handler = GetAction(url);
                if (handler != null)
                {
                    server.Log(EventArgs.LogType.Error, "{0} already exists!duplicate definition {1}.{2}!", url, controllerType.Name,
                        mi.Name);
                    continue;
                }
                handler = new ActionHandler(obj, mi);
                handler.NoConvert = noconvert;
                handler.SingleInstance = ca.SingleInstance;
                handler.DataConvert = actionConvert;
                handler.Route = ra;
                handler.Method = method;
                handler.SourceUrl = sourceUrl;
                handler.Filters.AddRange(filters);
                fas = mi.GetCustomAttributes<FilterAttribute>(false);
                handler.Filters.AddRange(fas);
                skipfilters = mi.GetCustomAttributes<SkipFilterAttribute>(false);
                foreach (SkipFilterAttribute item in skipfilters)
                {
                    RemoveFilter(handler.Filters, item.Types);
                }
                mMethods[url] = handler;
                server.Log(EventArgs.LogType.Info, "register {0}.{1} to {2}", controllerType.Name, mi.Name, url);
            }

        }


        private ActionHandler GetAction(string url)
        {
            ActionHandler result = null;
            mMethods.TryGetValue(url, out result);
            return result;
        }


        public ActionResult ExecuteWithWS(HttpRequest request, HttpApiServer server, JToken token)
        {
            ActionResult result = new ActionResult();
            JToken url = token["url"];
            WebSockets.DataFrame dataFrame = server.CreateDataFrame(result);
            if (url == null)
            {
                if (server.EnableLog(EventArgs.LogType.Warring))
                    server.BaseServer.Log(EventArgs.LogType.Warring, request.Session, "websocket {0} not support, url info notfound!", request.ClientIPAddress);
                result.Code = 403;
                result.Error = "not support, url info notfound!";
                request.Session.Send(dataFrame);
                return result;
            }
            result.Url = url.Value<string>();
            string baseurl = result.Url;
            if (server.ServerConfig.UrlIgnoreCase)
                baseurl = HttpParse.CharToLower(result.Url);
            if (baseurl[0] != '/')
                baseurl = "/" + baseurl;
            result.Url = baseurl;
            JToken data = token["params"];
            if (data == null)
                data = (JToken)Newtonsoft.Json.JsonConvert.DeserializeObject("{}");
            JToken requestid = data["_requestid"];
            if (requestid != null)
                result.ID = requestid.Value<string>();
            ActionHandler handler = GetAction(baseurl);
            if (handler == null)
            {
                if (server.EnableLog(EventArgs.LogType.Warring))
                    server.BaseServer.Log(EventArgs.LogType.Warring, request.Session, "websocket {0} execute {1} notfound", request.ClientIPAddress, result.Url);
                result.Code = 404;
                result.Error = "url " + baseurl + " notfound!";
                request.Session.Send(dataFrame);
            }
            else
            {
                try
                {
                    Data.DataContxt dataContxt = new Data.DataContxt();
                    DataContextBind.BindJson(dataContxt, data);
                    WebsocketJsonContext dc = new WebsocketJsonContext(server, request, dataContxt);
                    dc.ActionUrl = baseurl;
                    dc.RequestID = result.ID;
                    ActionContext context = new ActionContext(handler, dc, this);
                    long startTime = server.BaseServer.GetRunTime();
                    context.Execute();
                    if (!dc.AsyncResult)
                    {
                        if (context.Result is ActionResult)
                        {
                            result = (ActionResult)context.Result;
                            result.ID = dc.RequestID;
                            if (result.Url == null)
                                result.Url = dc.ActionUrl;
                            dataFrame.Body = result;
                        }
                        else
                        {
                            result.Data = context.Result;
                        }
                        dataFrame.Send(request.Session);
                        if (server.EnableLog(EventArgs.LogType.Info))
                            server.BaseServer.Log(EventArgs.LogType.Info, request.Session, "{0} ws execute {1} action use time:{2}ms", request.ClientIPAddress,
                                dc.ActionUrl, server.BaseServer.GetRunTime() - startTime);

                    }
                }
                catch (Exception e_)
                {
                    if (server.EnableLog(EventArgs.LogType.Error))
                        server.BaseServer.Log(EventArgs.LogType.Error, request.Session, "websocket {0} execute {1} inner error {2}@{3}", request.ClientIPAddress, request.Url, e_.Message, e_.StackTrace);
                    result.Code = 500;
                    result.Error = e_.Message;
                    if (server.ServerConfig.OutputStackTrace)
                    {
                        result.StackTrace = e_.StackTrace;
                    }
                    dataFrame.Send(request.Session);
                }
            }
            return result;
        }


        public void Execute(HttpRequest request, HttpResponse response, HttpApiServer server)
        {
            ActionHandler handler = GetAction(request.BaseUrl);
            if (handler == null)
            {
                if (server.EnableLog(EventArgs.LogType.Warring))
                    server.BaseServer.Log(EventArgs.LogType.Warring, request.Session, "{0} execute {1} action  not found", request.ClientIPAddress, request.Url);
                if (!server.OnHttpRequesNotfound(request, response).Cancel)
                {
                    NotFoundResult notFoundResult = new NotFoundResult("{0} action not found", request.Url);
                    response.Result(notFoundResult);
                }
            }
            else
            {
                try
                {
                    if (request.Method != handler.Method)
                    {
                        if (server.EnableLog(EventArgs.LogType.Warring))
                            server.BaseServer.Log(EventArgs.LogType.Warring, request.Session, "{0} execute {1} action  {1} not support", request.ClientIPAddress, request.Url, request.Method);
                        NotSupportResult notSupportResult = new NotSupportResult("{0} action not support {1}", request.Url, request.Method);
                        response.Result(notSupportResult);
                        return;
                    }
                    if (!handler.NoConvert && handler.DataConvert == null)
                    {
                        handler.DataConvert = DataContextBind.GetConvertAttribute(request.ContentType);
                    }
                    if (!handler.NoConvert)
                        handler.DataConvert.Execute(request.Data, request);
                    HttpContext pc = new HttpContext(server, request, response, request.Data);
                    long startTime = server.BaseServer.GetRunTime();
                    pc.ActionUrl = request.BaseUrl;
                    ActionContext context = new ActionContext(handler, pc, this);
                    context.Execute();
                    if (!response.AsyncResult)
                    {
                        object result = context.Result;
                        response.Result(result);
                        if (server.EnableLog(EventArgs.LogType.Info))
                            server.BaseServer.Log(EventArgs.LogType.Info, request.Session, "{0} http execute {1} action use time:{2}ms", request.ClientIPAddress,
                                request.BaseUrl, server.BaseServer.GetRunTime() - startTime);
                    }
                }
                catch (Exception e_)
                {
                    InnerErrorResult result = new InnerErrorResult($"http execute {request.BaseUrl} action error ", e_, server.ServerConfig.OutputStackTrace);
                    response.Result(result);

                    if (server.EnableLog(EventArgs.LogType.Error))
                        response.Session.Server.Log(EventArgs.LogType.Error, response.Session, "{0} execute {1} action inner error {2}@{3}", request.ClientIPAddress, request.Url, e_.Message, e_.StackTrace);
                }
            }
        }
    }
}
