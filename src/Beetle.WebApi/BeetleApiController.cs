﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;

namespace Beetle.WebApi {
    using Meta;
    using Server;
    using Server.Interface;
    using ServerHelper = Server.Helper;

    public abstract class BeetleApiController<TContextHandler> : BeetleApiController, IBeetleService<TContextHandler>
            where TContextHandler : class, IContextHandler {

        protected BeetleApiController() {
        }

        protected BeetleApiController(TContextHandler contextHandler)
            : this(contextHandler, null) {
        }

        protected BeetleApiController(IBeetleApiConfig config)
            : this(null, config) {
        }

        protected BeetleApiController(TContextHandler contextHandler, IBeetleApiConfig config) : base(config) {
            ContextHandler = contextHandler;
        }

        public TContextHandler ContextHandler { get; private set; }

        IContextHandler IBeetleService.ContextHandler => ContextHandler;

        protected override void Initialize(HttpControllerContext controllerContext) {
            base.Initialize(controllerContext);

            if (ContextHandler == null) {
                ContextHandler = CreateContextHandler();
            }
            ContextHandler.Initialize();
        }

        protected virtual TContextHandler CreateContextHandler() {
            return Activator.CreateInstance<TContextHandler>();
        }
    }

    [BeetleQueryable]
    public abstract class BeetleApiController : ApiController, IBeetleService, IODataService {

        protected BeetleApiController() : this(null) {
        }

        protected BeetleApiController(IBeetleApiConfig config) {
            Config = config ?? new BeetleApiConfig();
        }

        public IBeetleApiConfig Config { get; }

        protected bool ForbidBeetleQueryString { get; set; }

        protected bool AutoHandleUnknownActions { get; set; }

        protected virtual Metadata GetMetadata() {
            var svc = (IBeetleService)this;
            return svc.ContextHandler?.Metadata();
        }

        [HttpGet]
        [BeetleQueryable(typeof(SimpleResultApiConfig))]
        public virtual object Metadata() {
            return GetMetadata()?.ToMinified();
        }

        public override Task<HttpResponseMessage> ExecuteAsync(HttpControllerContext controllerContext,
                                                               CancellationToken cancellationToken) {
            if (!AutoHandleUnknownActions)
                return base.ExecuteAsync(controllerContext, cancellationToken);

            try {
                return base.ExecuteAsync(controllerContext, cancellationToken);
            }
            catch (HttpResponseException ex) {
                if (ex.Response.StatusCode != HttpStatusCode.NotFound) throw;

                var action = controllerContext.Request.GetRouteData().Values["action"].ToString();
                var content = HandleUnknownAction(action);
                var responseMessage = new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = content };
                return Task.FromResult(responseMessage);
            }
        }

        protected virtual ObjectContent HandleUnknownAction(string action) {
            var contextHandler = ((IBeetleService)this).ContextHandler;
            if (contextHandler == null)
                throw new NotSupportedException();

            var result = contextHandler.HandleUnknownAction(action);
            Helper.GetParameters(Config, out IList<BeetleParameter> parameters);

            var actionContext = new ActionContext(
                action, result, parameters,
                MaxResultCount, null, this
            );
            var processResult = ProcessRequest(actionContext);
            Helper.SetCustomHeaders(processResult);
            return Helper.HandleResponse(processResult);
        }

        protected virtual IList<EntityBag> ResolveEntities(object saveBundle,
                                                           out IList<EntityBag> unknownEntities) {
            return ServerHelper.ResolveEntities(saveBundle, Config, GetMetadata(), out unknownEntities);
        }

        protected virtual IEnumerable<EntityBag> HandleUnknowns(IEnumerable<EntityBag> unknowns) {
            return Enumerable.Empty<EntityBag>();
        }

        protected virtual Task<SaveResult> SaveChanges(SaveContext saveContext) {
            var svc = (IBeetleService)this;
            return svc.ContextHandler?.SaveChanges(saveContext)
                   ?? throw new NotSupportedException();
        }

        #region Implementation of IBeetleService

        IBeetleConfig IBeetleService.Config => Config;

        IContextHandler IBeetleService.ContextHandler => null;

        public int? MaxResultCount { get; set; }

        Metadata IBeetleService.Metadata() {
            return GetMetadata();
        }

        public virtual object CreateType(string typeName, string initialValues) {
            return ServerHelper.CreateType(typeName, initialValues, Config);
        }

        ProcessResult IBeetleService.ProcessRequest(ActionContext actionContext) {
            return ProcessRequest(actionContext);
        }

        protected virtual ProcessResult ProcessRequest(ActionContext actionContext) {
            var svc = (IBeetleService)this;
            return svc.ContextHandler != null
                ? svc.ContextHandler.ProcessRequest(actionContext)
                : ServerHelper.DefaultRequestProcessor(actionContext);
        }

        [HttpPost]
        public async Task<SaveResult> SaveChanges(object saveBundle) {
            var entityBags = ResolveEntities(saveBundle, out IList<EntityBag> unknowns);
            var entityBagList = entityBags == null
                ? new List<EntityBag>()
                : entityBags as List<EntityBag> ?? entityBags.ToList();

            var handledUnknowns = HandleUnknowns(unknowns);
            if (handledUnknowns != null) {
                entityBagList.AddRange(handledUnknowns);
            }
            if (!entityBagList.Any()) return SaveResult.Empty;

            var saveContext = new SaveContext(entityBagList);
            OnBeforeSaveChanges(new BeforeSaveEventArgs(saveContext));
            var retVal = await SaveChanges(saveContext);
            OnAfterSaveChanges(new AfterSaveEventArgs(retVal));

            return retVal;
        }

        #region Event-Handlers

        public event BeforeQueryExecuteDelegate BeforeHandleQuery;

        void IBeetleService.OnBeforeHandleQuery(BeforeQueryExecuteEventArgs args) {
            OnBeforeHandleQuery(args);
        }

        protected virtual void OnBeforeHandleQuery(BeforeQueryExecuteEventArgs args) {
            BeforeHandleQuery?.Invoke(this, args);
        }

        public event BeforeQueryExecuteDelegate BeforeQueryExecute;

        void IBeetleService.OnBeforeQueryExecute(BeforeQueryExecuteEventArgs args) {
            OnBeforeQueryExecute(args);
        }

        protected virtual void OnBeforeQueryExecute(BeforeQueryExecuteEventArgs args) {
            BeforeQueryExecute?.Invoke(this, args);
        }

        public event AfterQueryExecuteDelegate AfterQueryExecute;

        void IBeetleService.OnAfterQueryExecute(AfterQueryExecuteEventArgs args) {
            OnAfterQueryExecute(args);
        }

        protected virtual void OnAfterQueryExecute(AfterQueryExecuteEventArgs args) {
            AfterQueryExecute?.Invoke(this, args);
        }

        public event BeforeSaveDelegate BeforeSaveChanges;

        protected virtual void OnBeforeSaveChanges(BeforeSaveEventArgs args) {
            BeforeSaveChanges?.Invoke(this, args);
        }

        public event AfterSaveDelegate AfterSaveChanges;

        protected virtual void OnAfterSaveChanges(AfterSaveEventArgs args) {
            AfterSaveChanges?.Invoke(this, args);
        }

        #endregion

        #endregion

        #region Implementation of IODataService

        public event BeforeODataQueryHandleDelegate BeforeODataQueryHandle;

        void IODataService.OnBeforeODataQueryHandle(BeforeODataQueryHandleEventArgs args) {
            BeforeODataQueryHandle?.Invoke(this, args);
        }

        protected void OnBeforeODataHandleQuery(BeforeODataQueryHandleEventArgs args) {
            ((IODataService)this).OnBeforeODataQueryHandle(args);
        }

        #endregion
    }
}