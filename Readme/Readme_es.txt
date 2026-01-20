Large Folder Finder
====================
Una herramienta para extraer y listar rápidamente carpetas más grandes que un tamaño especificado.


■ Cómo usar
--------------------
1. Seleccione la carpeta que desea investigar.
2. Especifique el tamaño mínimo que desea extraer.
3. Presione el botón "Scan" para comenzar la búsqueda.
4. Los resultados se muestran en formato de texto.
5. Presione el botón de copia (ícono 📄) en la parte superior derecha para copiar los resultados al portapapeles.


■ Configuración Avanzada (Config.txt)
--------------------
Al editar "Config.txt" en el directorio de la aplicación, puede configurar un comportamiento detallado.
Haga clic en el botón "⚙" en la interfaz de usuario para abrirlo inmediatamente con un editor de texto como el Bloc de notas.
La configuración debe seguir el formato YAML. Si desea agregar sus propios comentarios, anteponga #.

    ▽ Elementos configurables: (Predeterminado)
    UseParallelScan: true
        Tipo: bool (true/false)
        Descripción: Habilitar el escaneo paralelo.
        Contexto (true): Efectivo para NAS (almacenamiento en red), etc. Dado que los SSD locales son rápidos, la sobrecarga de la paralelización podría ser mayor.

    SkipFolderCount: false
        Tipo: bool (true/false)
        Descripción: Si se debe omitir el conteo previo para la visualización del progreso y comenzar a escanear de inmediato.
        Si se establece en true, no se puede mostrar el porcentaje de progreso porque se desconoce el número total de carpetas.

    MaxDepthForCount: 3
        Tipo: int (número natural)
        Descripción: Profundidad de jerarquía máxima para el conteo previo de carpetas para determinar el porcentaje de progreso.
        Valores más altos pueden requerir más tiempo pero aumentan la precisión del progreso.
        Ejemplo (3): NAS: 3~6, PC interno: 7~

    UsePhysicalSize: true
        Tipo: bool (true/false)
        Descripción: Determina si se calcula el "tamaño asignado en disco" considerando el tamaño del clúster.
        Ejemplo (true): Normalmente se recomienda mantenerlo en true. Los resultados serán más cercanos a las visualizaciones de propiedades de Windows. Si es false, se calcula por el tamaño real del archivo.
        Antes de ajustar esto, recomendamos ejecutar la aplicación como administrador para incluir con precisión los archivos del sistema en los cálculos.


■ Cómo agregar archivos de idioma
--------------------
Esta herramienta admite múltiples idiomas y puede agregar nuevos.
1. Abra la carpeta "Languages" en el mismo directorio que el ejecutable (.exe).
2. Copie un archivo existente como "en.yaml" y cámbiele el nombre al código de cultura del idioma que desea agregar (por ejemplo, "fr.yaml" para francés).
   * Consulte la documentación de Microsoft para obtener una lista de códigos de cultura:
   https://learn.microsoft.com/es-es/windows-hardware/manufacture/desktop/available-language-packs-for-windows?view=windows-11
3. Edite el texto dentro del archivo YAML (guarde en formato UTF-8).
4. Reinicie la aplicación y el nuevo idioma aparecerá en el menú "Language".
* Si es necesario, cree y agregue un Readme_<code>.txt consultando otros archivos.


■ Desinstalación Limpia (Eliminar configuraciones y registros)
--------------------
Para eliminar completamente las configuraciones y los registros de ejecución de esta herramienta, elimine manualmente la siguiente carpeta:
%LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder
(Puede abrirla directamente pegando la ruta anterior en la barra de direcciones del Explorador)


■ Copyright
--------------------
Copyright (C) 2026 Ryoma Henzan / Cat & Chocolate Laboratory
