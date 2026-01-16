Large Folder Finder
====================
Uma ferramenta para extrair e listar rapidamente pastas maiores que um tamanho especificado.


■ Como usar
--------------------
1. Selecione a pasta que deseja investigar.
2. Especifique o tamanho mínimo que deseja extrair.
3. Pressione o botão "Scan" para iniciar a pesquisa.
4. Os resultados são exibidos em formato de texto.
5. Pressione o botão de cópia (ícone 📄) no canto superior direito para copiar os resultados para a área de transferência.


■ Configurações Avançadas (Config.txt)
--------------------
Ao editar o "Config.txt" no diretório do aplicativo, você pode configurar comportamentos detalhados.
Clique no botão "⚙" na interface do usuário para abri-lo imediatamente com um editor de texto como o Bloco de Notas.
A configuração deve seguir o formato YAML. Se você quiser adicionar seus próprios comentários, use o prefixo #.

    ▽ Itens configuráveis: (Padrão)
    UseParallelScan: false
        Tipo: bool (true/false)
        Descrição: Habilitar verificação paralela.
        Contexto (false): HDDs (e NAS) são fisicamente rotativos e fracos com acesso paralelo, então defina como false. Recomendado como "true" apenas para SSDs.

    SkipFolderCount: false
        Tipo: bool (true/false)
        Descrição: Se deve pular a contagem prévia para exibição de progresso e iniciar a verificação imediatamente.
        Se definido como true, a porcentagem de progresso não pode ser exibida porque o número total de pastas é desconhecido.

    MaxDepthForCount: 3
        Tipo: int (número natural)
        Descrição: Profundidade máxima da hierarquia para contagem prévia de pastas para determinar a porcentagem de progresso.
        Valores maiores podem levar mais tempo, mas aumentam a precisão do progresso.
        Exemplo (3): NAS: 3~6, PC interno: 7~

    UsePhysicalSize: true
        Tipo: bool (true/false)
        Descrição: Se deve calcular o "tamanho alocado no disco" considerando o tamanho do cluster.
        Exemplo (true): Normalmente recomendado manter como true. Os resultados serão mais próximos da exibição de propriedades do Windows. Se for false, ele calcula pelo tamanho real do arquivo.
        Antes de ajustar isso, recomendamos executar o aplicativo como administrador para incluir arquivos do sistema com precisão nos cálculos.


■ Como adicionar arquivos de idioma
--------------------
Esta ferramenta suporta vários idiomas e você pode adicionar novos.
1. Abra a pasta "Languages" no mesmo diretório que o executável (.exe).
2. Copie um arquivo existente como "en.yaml" e renomeie-o com o código de cultura do idioma que deseja adicionar (por ejemplo, "fr.yaml" para francês).
   * Consulte a documentação da Microsoft para obter uma lista de códigos de cultura:
   https://learn.microsoft.com/pt-br/windows-hardware/manufacture/desktop/available-language-packs-for-windows?view=windows-11
3. Edite o texto dentro do arquivo YAML (salve no formato UTF-8).
4. Reinicie o aplicativo e o novo idioma aparecerá no menu "Language".
* Se necessário, crie e adicione um Readme_<code>.txt consultando outros arquivos.


■ Desinstalação Limpia (Remover Configurações e Logs)
--------------------
Para remover completamente as configurações e os logs de execução desta ferramenta, exclua manualmente a seguinte pasta:
%LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder
(Você pode abri-la diretamente colando o caminho acima na barra de endereços do Explorer)


■ Copyright
--------------------
Copyright (C) 2026 Ryoma Henzan / Cat & Chocolate Laboratory
