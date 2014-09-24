﻿using Beetle.Server.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using Beetle.Server.Meta;

namespace Beetle.Server {

    /// <summary>
    /// Base context handler class. ORM context (manager etc.) proxy.
    /// Services use this class' implementors to handle data-metadata operations.
    /// Needed to be implemented for each used ORM.
    /// </summary>
    /// <typeparam name="TContext">The type of the context.</typeparam>
    public abstract class ContextHandler<TContext> : ContextHandler, IContextHandler<TContext> {

        /// <summary>
        /// Initializes a new instance of the <see cref="ContextHandler{TContext}"/> class.
        /// </summary>
        protected ContextHandler() {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ContextHandler{TContext}"/> class.
        /// </summary>
        /// <param name="context">The injected context.</param>
        protected ContextHandler(TContext context) {
            Context = context;
        }

        /// <summary>
        /// Initializes this instance.
        /// </summary>
        public override void Initialize() {
            if (Equals(Context, default(TContext)))
                Context = CreateContext();
        }

        /// <summary>
        /// Creates the context.
        /// </summary>
        /// <returns></returns>
        public virtual TContext CreateContext() {
            return Activator.CreateInstance<TContext>();
        }

        /// <summary>
        /// Gets the entity framework context.
        /// </summary>
        /// <value>
        /// The context.
        /// </value>
        public TContext Context { get; private set; }
    }

    /// <summary>
    /// Base context handler class. ORM context (manager etc.) proxy.
    /// Services use this class' implementors to handle data-metadata operations.
    /// Needed to be implemented for each used ORM.
    /// </summary>
    public abstract class ContextHandler : IContextHandler {

        /// <summary>
        /// Initializes this instance.
        /// </summary>
        public virtual void Initialize() {
        }

        /// <summary>
        /// Handles the unknown action.
        /// </summary>
        /// <param name="action">The action.</param>
        /// <returns></returns>
        /// <exception cref="System.NotImplementedException"></exception>
        public virtual object HandleUnknownAction(string action) {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Return metadata about data structure.
        /// </summary>
        /// <returns>Metadata object.</returns>
        public abstract Metadata Metadata();

        /// <summary>
        /// Creates the type by name.
        /// </summary>
        /// <param name="typeName">Name of the type.</param>
        /// <returns></returns>
        /// <exception cref="System.ArgumentException"></exception>
        public virtual object CreateType(string typeName) {
            var type = Type.GetType(typeName);
            if (type == null) throw new ArgumentException(string.Format(Resources.TypeCouldNotBeFound, typeName));
            return Activator.CreateInstance(type);
        }

        /// <summary>
        /// Processes the request.
        /// </summary>
        /// <param name="contentValue">The content value.</param>
        /// <param name="queryParameters">The query parameters.</param>
        /// <param name="actionContext">The action context.</param>
        /// <param name="service">The service.</param>
        /// <returns></returns>
        public virtual ProcessResult ProcessRequest(object contentValue, IEnumerable<KeyValuePair<string, string>> queryParameters,
                                                    ActionContext actionContext, IBeetleService service) {
            var queryable = contentValue as IQueryable;
            if (queryable != null)
                return QueryableHandler.HandleContent(queryable, queryParameters, actionContext, service, this);

            return new ProcessResult { Result = contentValue };
        }

        /// <summary>
        /// Handles the unmapped objects (which does not mapped to persistence layer, like DTOs or Proxies).
        /// </summary>
        /// <param name="unmappeds">The unmapped objects.</param>
        public virtual IEnumerable<EntityBag> HandleUnmappeds(IEnumerable<EntityBag> unmappeds) {
            var retVal = new List<EntityBag>();
            foreach (var unmapped in unmappeds) {
                var client = MapToEntity(unmapped.ClientEntity);
                if (client == null) continue;

                var server = MapToEntity(unmapped.Entity);
                if (server == null) continue;

                var originalValues = unmapped.OriginalValues == null ? null : MapProperties(unmapped.Entity, unmapped.OriginalValues);

                retVal.Add(new EntityBag(client, server, unmapped.EntityState, originalValues, unmapped.Index, null, unmapped.ForceUpdate));
            }
            return retVal;
        }

        /// <summary>
        /// Maps unknown object to entity.
        /// </summary>
        /// <param name="unmapped">The unmapped.</param>
        /// <returns>Mapped entity.</returns>
        public virtual object MapToEntity(object unmapped) {
            return null;
        }

        /// <summary>
        /// Maps unknown objects original value properties to entity properties.
        /// </summary>
        /// <param name="unmapped">The unmapped.</param>
        /// <param name="unmappedOriginalValues">The unmapped object's original values.</param>
        /// <returns>New dictionary which represents values for mapped entity.</returns>
        public virtual IDictionary<string, object> MapProperties(object unmapped, IDictionary<string, object> unmappedOriginalValues) {
            return null;
        }

        /// <summary>
        /// Gets the generated values for handled unmapped objects.
        /// </summary>
        /// <returns></returns>
        public virtual IEnumerable<GeneratedValue> GetHandledUnmappedGeneratedValues(IEnumerable<EntityBag> handledUnmappeds) {
            return Enumerable.Empty<GeneratedValue>();
        } 

        /// <summary>
        /// Saves the changes.
        /// </summary>
        /// <param name="entities">The entities.</param>
        /// <returns>
        /// Save result.
        /// </returns>
        public abstract SaveResult SaveChanges(IEnumerable<EntityBag> entities);

        /// <summary>
        /// Gets query handler.
        /// </summary>
        public virtual QueryableHandler QueryableHandler {
            get { return QueryableHandler.Instance; }
        }

        /// <summary>
        /// Gets the changed values.
        /// </summary>
        /// <param name="entityBags">The entity bags.</param>
        /// <returns>All generated values while saving given entity bags.</returns>
        public virtual IEnumerable<GeneratedValue> GetGeneratedValues(IEnumerable<EntityBag> entityBags) {
            return Helper.GetGeneratedValues(entityBags, Metadata());
        }

        /// <summary>
        /// Occurs when [before handle query].
        /// </summary>
        public event BeforeQueryExecuteDelegate BeforeHandleQuery;

        /// <summary>
        /// Called when [before handle query].
        /// </summary>
        /// <param name="args">Before query execute event arguments.</param>
        void IContextHandler.OnBeforeHandleQuery(BeforeQueryExecuteEventArgs args) {
            OnBeforeHandleQuery(args);
        }

        /// <summary>
        /// Called when [before handle query].
        /// </summary>
        /// <param name="args">Before query execute event arguments.</param>
        protected virtual void OnBeforeHandleQuery(BeforeQueryExecuteEventArgs args) {
            var handler = BeforeHandleQuery;
            if (handler != null)
                handler(this, args);
        }

        /// <summary>
        /// Occurs when [before execute query].
        /// </summary>
        public event BeforeQueryExecuteDelegate BeforeQueryExecute;

        /// <summary>
        /// Called when [before query execute].
        /// </summary>
        /// <param name="args">Before query execute event arguments.</param>
        void IContextHandler.OnBeforeQueryExecute(BeforeQueryExecuteEventArgs args) {
            OnBeforeQueryExecute(args);
        }

        /// <summary>
        /// Called when [before query execute].
        /// </summary>
        /// <param name="args">Before query execute event arguments.</param>
        protected virtual void OnBeforeQueryExecute(BeforeQueryExecuteEventArgs args) {
            var handler = BeforeQueryExecute;
            if (handler != null)
                handler(this, args);
        }

        /// <summary>
        /// Occurs when [after execute query].
        /// </summary>
        public event AfterQueryExecuteDelegate AfterQueryExecute;

        /// <summary>
        /// Called when [after query execute].
        /// </summary>
        /// <param name="args">After query execute event arguments.</param>
        void IContextHandler.OnAfterQueryExecute(AfterQueryExecuteEventArgs args) {
            OnAfterQueryExecute(args);
        }

        /// <summary>
        /// Called when [after query execute].
        /// </summary>
        /// <param name="args">After query execute event arguments.</param>
        protected virtual void OnAfterQueryExecute(AfterQueryExecuteEventArgs args) {
            var handler = AfterQueryExecute;
            if (handler != null)
                handler(this, args);
        }

        /// <summary>
        /// Occurs before save.
        /// </summary>
        public event ContextSaveDelegate BeforeSaveChanges;

        /// <summary>
        /// Called when [before save changes].
        /// </summary>
        /// <param name="entities">The entities.</param>
        protected virtual void OnBeforeSaveChanges(IEnumerable<EntityBag> entities) {
            var handler = BeforeSaveChanges;
            if (handler != null) handler(this, new ContextSaveEventArgs(entities));
        }

        /// <summary>
        /// Occurs after save.
        /// </summary>
        public event ContextSaveDelegate AfterSaveChanges;

        /// <summary>
        /// Called when [after save changes].
        /// </summary>
        /// <param name="entities">The entities.</param>
        protected virtual void OnAfterSaveChanges(IEnumerable<EntityBag> entities) {
            var handler = AfterSaveChanges;
            if (handler != null) handler(this, new ContextSaveEventArgs(entities));
        }

        /// <summary>
        /// Gets or sets a value indicating whether [validate on save enabled].
        /// </summary>
        /// <value>
        /// <c>true</c> if [validate on save enabled]; otherwise, <c>false</c>.
        /// </value>
        public bool ValidateOnSaveEnabled { get; set; }
    }
}