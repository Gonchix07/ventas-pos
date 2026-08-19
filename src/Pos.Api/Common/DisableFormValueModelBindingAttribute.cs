using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Pos.Api.Common;

/// <summary>
/// Saca los value providers de formulario de la acción.
/// <para>Hace falta para poder leer un multipart "a mano" con MultipartReader: MVC, ante un request
/// con content-type de formulario, llama a <c>ReadFormAsync()</c> para armar los value providers
/// ANTES de entrar a la acción — aunque la acción no tenga ningún parámetro que venga del form. Eso
/// consume el body entero (y lo bufferea), así que después el MultipartReader encuentra el stream
/// vacío y falla con "Unexpected end of Stream".</para>
/// </summary>
public class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var factories = context.ValueProviderFactories;
        factories.RemoveType<FormValueProviderFactory>();
        factories.RemoveType<FormFileValueProviderFactory>();
        factories.RemoveType<JQueryFormValueProviderFactory>();
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
