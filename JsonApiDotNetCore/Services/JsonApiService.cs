using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JsonApiDotNetCore.Abstractions;
using Microsoft.AspNetCore.Http;
using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.JsonApi;
using Newtonsoft.Json;

namespace JsonApiDotNetCore.Services
{
  public class JsonApiService
  {
    private readonly JsonApiModelConfiguration _jsonApiModelConfiguration;
    private IServiceProvider _serviceProvider;

    public JsonApiService(JsonApiModelConfiguration configuration)
    {
      _jsonApiModelConfiguration = configuration;
    }

    public bool HandleJsonApiRoute(HttpContext context, IServiceProvider serviceProvider)
    {
      _serviceProvider = serviceProvider;

      var route = context.Request.Path;
      var requestMethod = context.Request.Method;
      var controllerMethodIdentifier = _jsonApiModelConfiguration.GetControllerMethodIdentifierForRoute(route, requestMethod);
      if (controllerMethodIdentifier == null) return false;
      CallControllerMethod(controllerMethodIdentifier, context);
      return true;
    }

    private void CallControllerMethod(ControllerMethodIdentifier controllerMethodIdentifier, HttpContext context)
    {
      var dbContext = _serviceProvider.GetService(_jsonApiModelConfiguration.ContextType);
      var jsonApiContext = new JsonApiContext(controllerMethodIdentifier.Route, dbContext);
      var controller = new JsonApiController(context, jsonApiContext);
      var resourceId = controllerMethodIdentifier.GetResourceId();
      switch (controllerMethodIdentifier.RequestMethod)
      {
        case "GET":
          if (string.IsNullOrEmpty(resourceId))
          {
            var result = controller.Get();
            SendResponse(context, SerializeResponse(jsonApiContext, result));
          }
          else
          {
            controller.Get(resourceId);
          }
          break;
        case "POST":
          controller.Post(null); // TODO: need the request body
          break;
        case "PUT":
          controller.Put(resourceId, null);
          break;
        case "DELETE":
          controller.Delete(resourceId);
          break;
        default:
          throw new ArgumentException("Request method not supported", nameof(controllerMethodIdentifier));
      }
    }

    private string SerializeResponse(JsonApiContext context, object result)
    {
      var response = new JsonApiDocument
      {
        Data = GetJsonApiDocumentData(context, result)
      };

      return JsonConvert.SerializeObject(response);
    }

    private object GetJsonApiDocumentData(JsonApiContext context, object result)
    {
      var enumerableResult = result as IEnumerable;

      if (enumerableResult == null) return ResourceToJsonApiDatum(context, result);

      var data = new List<JsonApiDatum>();
      foreach (var resource in enumerableResult)
      {
        data.Add(ResourceToJsonApiDatum(context, resource));
      }
      return data;
    }

    private JsonApiDatum ResourceToJsonApiDatum(JsonApiContext context, object resource)
    {
      return new JsonApiDatum
      {
        Type = context.Route.ContextPropertyName,
        Id = ((IJsonApiResource)resource).Id,
        Attributes = GetAttributesFromResource(resource)
      };
    }

    private static Dictionary<string, object> GetAttributesFromResource(object resource)
    {
      return resource.GetType().GetProperties()
        .Where(propertyInfo => propertyInfo.GetMethod.IsVirtual == false)
        .ToDictionary(
          propertyInfo => propertyInfo.Name, propertyInfo => propertyInfo.GetValue(resource)
        );
    }

    private void SendResponse(HttpContext context, string content)
    {
      context.Response.WriteAsync(content);
      context.Response.Body.Flush();
    }
  }
}