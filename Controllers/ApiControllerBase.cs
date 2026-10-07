using HorseVillage.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace HorseVillage.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected Guid CurrentUserId => User.GetUserId();

    // Problem details with a stable machine-readable "code" for the frontend.
    protected ObjectResult Error(int statusCode, string code, string detail)
    {
        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, statusCode, detail: detail);
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = statusCode };
    }
}
