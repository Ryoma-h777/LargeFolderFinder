Large Folder Finder
====================
Una herramienta para analizar y listar rápidamente jerarquías de carpetas.
Útil para el análisis de carpetas utilizando condiciones de tamaño y filtros (comodines, expresiones regulares).
Ayuda a identificar la causa de problemas en datos grandes utilizados por múltiples usuarios, como NAS.


■ Cómo usar
--------------------
  1. Seleccione la carpeta que desea investigar.
  2. Presione el botón "▶" (Escanear) para iniciar la búsqueda.
  3. Los resultados se muestran en un formato similar al Explorador de Windows.
  4. Especifique las condiciones de visualización: tamaño mínimo a extraer, filtro, ordenar, contraer carpetas.
  5. Presione el botón de copiar en la parte superior derecha para copiar los resultados de visualización al portapapeles.
  6. Presione el botón "+" a la derecha de la pestaña para iniciar un nuevo escaneo manteniendo el historial.
    El historial se conserva incluso después de cerrar la aplicación.
※ Ejecutar la aplicación con privilegios de administrador le permite analizar carpetas con privilegios de administrador dentro de la unidad C.
※ Puede cambiar el idioma o cambiar el diseño desde Menú/Ver.
※ Puede cambiar la configuración desde Menú/Ver/Abrir configuración avanzada(S). Los detalles se describen a continuación.


■ Acerca de las funciones de visualización
-------------------
1. Ordenar
  Haga clic en cada etiqueta (Nombre, Tamaño, Fecha de modificación, Tipo) para ordenar el orden de visualización.
  Haga clic nuevamente para alternar entre ascendente/descendente.
2. Mostrar archivos
  Marque esto para mostrar también archivos.
3. Tamaño mínimo
  Especifique el tamaño mínimo de las carpetas o archivos a mostrar. Se mostrarán los elementos iguales o mayores que el valor establecido.
  Ingrese 0 si desea mostrar todo.
  Las unidades se pueden seleccionar desde Byte hasta TB.
4. Filtro
Comodín: Mismo comportamiento que el Explorador de Windows.
  * Permite coincidir con cualquier cadena. Ejemplo) *.txt Todos los archivos txt con cualquier nombre. Ejemplo 2) *datos* Todos los archivos con "datos" en el nombre.
  ? Permite coincidir con cualquier carácter individual. Ejemplo) 202?año → 2020año~2029año, etc. (también coincide con no dígitos)
  ~ Coloque antes de (* o ?) para buscar esos caracteres en sí. Ejemplo) ~?.txt → Busca ?.txt
Expresión regular: Función de filtro avanzada (utilizada por ingenieros, etc.)
  Puede hacer cosas que los comodines no pueden. Coincidir solo números, letras minúsculas, letras mayúsculas, extraer solo elementos que no coinciden, etc.
  Es complejo, así que busque "cómo usar expresiones regulares" por separado.
  También hay herramientas de verificación de expresiones regulares disponibles para verificar si su búsqueda funciona correctamente.
5. Espacio/Tabulación
  Especifique si llenar el espacio entre nombre y tamaño con espacios o tabulaciones al presionar el botón de copiar.


■ Cuando no funciona correctamente
------------------------
※ Puede verificar el comportamiento de la aplicación desde Menú/Ver/Registros.
※ Si la aplicación se comporta de manera extraña, eliminar los datos en la siguiente carpeta puede restablecer el caché y restaurar la funcionalidad.
    %LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder


■ Acerca de la configuración avanzada (Config.txt)
--------------------
Al editar "Config.txt" en el directorio de ejecución, son posibles configuraciones de comportamiento más detalladas.
Haga clic en el botón "⚙" en la interfaz de usuario para abrirlo inmediatamente con un editor de texto como el Bloc de notas.
La configuración debe seguir el formato YAML. Si desea agregar sus propios comentarios, anteponga # antes.

    ▽ Elementos configurables: (Predeterminado)
    UseParallelScan: true
        Tipo: bool (true/false)
        Descripción: Habilitar procesamiento paralelo
        Valor esperado (true): Efectivo para NAS (almacenamiento en red), etc. Los SSD locales son rápidos, por lo que la sobrecarga de paralelización puede ser mayor.

    SkipFolderCount: false
        Tipo: bool (true/false)
        Descripción: Si omitir el recuento previo para la visualización del progreso e iniciar el escaneo inmediatamente
        Si es true, el porcentaje de progreso no se puede mostrar porque el número total es desconocido.

    MaxDepthForCount: 3
        Tipo: int (número natural)
        Descripción: Profundidad máxima de jerarquía para el recuento previo de carpetas para determinar el porcentaje de progreso
        Una jerarquía especificada mayor puede llevar más tiempo. En cambio, mejora la precisión del progreso.
        Valor esperado (3): NAS: 3~6, PC interno: 7~

    UsePhysicalSize: true
        Tipo: bool (true/false)
        Descripción: Si calcular el "tamaño asignado en disco" considerando el tamaño del clúster
        Valor esperado (true): Generalmente se recomienda mantener true. Los resultados estarán más cerca de las visualizaciones de propiedades de Windows. Si es false, se calcula por tamaño de archivo.
        Antes de ajustar esto, recomendamos ejecutar como administrador. Los archivos del sistema se incluirán en los cálculos para mayor precisión.

    OldDataThresholdDays: 30
        Tipo: int (Entero no negativo)
        Descripción: Resalta la pestaña en amarillo para indicar datos de escaneo antiguos si ha pasado el número de días especificado.
        Valor esperado: Preferencia del usuario.

■ Cómo agregar archivos de idioma
--------------------
Esta herramienta admite múltiples idiomas y puede agregar nuevos.
1. Abra la carpeta "Languages" en la misma jerarquía que el ejecutable de la aplicación (.exe).
2. Copie un archivo existente como "en.yaml" y cambie el nombre al código de cultura del idioma que desea agregar (por ejemplo, "fr.yaml" para francés).
   * Consulte lo siguiente para obtener una lista de códigos de cultura (por ejemplo: ja-JP / ja):
   https://learn.microsoft.com/es-es/windows-hardware/manufacture/desktop/available-language-packs-for-windows?view=windows-11
3. Edite el texto dentro del archivo YAML (guarde en formato UTF-8).
4. Reinicie la aplicación y el nuevo idioma aparecerá en el menú "Language".
※ Si es necesario, cree y agregue Readme_<language_code>.txt consultando otros archivos.


■ Desinstalación completa (Eliminar configuración y registros)
--------------------
Para eliminar completamente la configuración y los registros de ejecución de esta herramienta, elimine manualmente la siguiente carpeta:
%LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder
(Puede abrirla directamente pegando la ruta anterior en la barra de direcciones del Explorador)


■ Copyright
--------------------
Copyright (C) 2026 Ryoma Henzan / Cat & Chocolate Laboratory
