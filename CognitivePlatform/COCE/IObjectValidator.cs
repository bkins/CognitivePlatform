using System;
using System.Collections.Generic;
using System.Linq;

namespace CognitivePlatform.Api.COCE;

public interface IObjectValidator
{
    bool CanValidate(Type targetType);

    ObjectValidationResult Validate(object target);
}

public interface IObjectValidator<in T> : IObjectValidator where T : class
{
    ObjectValidationResult Validate(T target);
}
