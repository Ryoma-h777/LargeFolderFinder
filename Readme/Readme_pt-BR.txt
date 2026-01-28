Large Folder Finder
====================
Uma ferramenta para analisar e listar rapidamente hierarquias de pastas.
Útil para análise de pastas usando condições de tamanho e filtros (curingas, expressões regulares).
Ajuda a identificar a causa de problemas em dados grandes usados por vários usuários, como NAS.


■ Como usar
--------------------
  1. Selecione a pasta que deseja investigar.
  2. Pressione o botão "▶" (Escanear) para iniciar a pesquisa.
  3. Os resultados são exibidos em um formato semelhante ao Windows Explorer.
  4. Especifique as condições de exibição: tamanho mínimo a extrair, filtro, classificar, recolher pastas.
  5. Pressione o botão de copiar no canto superior direito para copiar os resultados de exibição para a área de transferência.
  6. Pressione o botão "+" à direita da guia para iniciar uma nova verificação mantendo o histórico.
    O histórico é mantido mesmo após fechar o aplicativo.
※ Executar o aplicativo com privilégios de administrador permite analisar pastas com privilégios de administrador dentro da unidade C.
※ Você pode mudar o idioma ou alterar o layout em Menu/Exibir.
※ Você pode alterar as configurações em Menu/Exibir/Abrir configurações avançadas(S). Os detalhes são descritos abaixo.


■ Sobre as funções de exibição
-------------------
1. Classificar
  Clique em cada rótulo (Nome, Tamanho, Data de modificação, Tipo) para classificar a ordem de exibição.
  Clique novamente para alternar entre crescente/decrescente.
2. Mostrar arquivos
  Marque isso para exibir também arquivos.
3. Tamanho mínimo
  Especifique o tamanho mínimo das pastas ou arquivos a serem exibidos. Itens iguais ou maiores que o valor definido serão exibidos.
  Digite 0 se quiser exibir tudo.
  As unidades podem ser selecionadas de Byte a TB.
4. Filtro
Curinga: Mesmo comportamento do Windows Explorer.
  * Permite corresponder a qualquer string. Exemplo) *.txt Todos os arquivos txt com qualquer nome. Exemplo 2) *dados* Todos os arquivos com "dados" no nome.
  ? Permite corresponder a qualquer caractere único. Exemplo) 202?ano → 2020ano~2029ano, etc. (também corresponde a não dígitos)
  ~ Coloque antes de (* ou ?) para pesquisar esses caracteres em si. Exemplo) ~?.txt → Pesquisa ?.txt
Expressão regular: Função de filtro avançada (usada por engenheiros, etc.)
  Pode fazer coisas que curingas não podem. Corresponder apenas números, letras minúsculas, letras maiúsculas, extrair apenas itens não correspondentes, etc.
  É complexo, então pesquise "como usar expressões regulares" separadamente.
  Também há ferramentas de verificação de expressões regulares disponíveis para verificar se sua pesquisa está funcionando corretamente.
5. Espaço/Tabulação
  Especifique se deve preencher o espaço entre nome e tamanho com espaços ou tabulações ao pressionar o botão de copiar.


■ Quando não funciona corretamente
------------------------
※ Você pode verificar o comportamento do aplicativo em Menu/Exibir/Logs.
※ Se o aplicativo se comportar de forma estranha, excluir os dados na seguinte pasta pode redefinir o cache e restaurar a funcionalidade.
    %LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder


■ Sobre configurações avançadas (Config.txt)
--------------------
Ao editar "Config.txt" no diretório de execução, configurações de comportamento mais detalhadas são possíveis.
Clique no botão "⚙" na interface do usuário para abri-lo imediatamente com um editor de texto como o Bloco de notas.
A configuração deve seguir o formato YAML. Se quiser adicionar seus próprios comentários, prefixe-os com #.

    ▽ Itens configuráveis: (Padrão)
    UseParallelScan: true
        Tipo: bool (true/false)
        Descrição: Habilitar processamento paralelo
        Valor esperado (true): Eficaz para NAS (armazenamento em rede), etc. SSDs locais são rápidos, então a sobrecarga de paralelização pode ser maior.

    SkipFolderCount: false
        Tipo: bool (true/false)
        Descrição: Se deve pular a pré-contagem para exibição de progresso e iniciar a verificação imediatamente
        Se true, a porcentagem de progresso não pode ser exibida porque o número total é desconhecido.

    MaxDepthForCount: 3
        Tipo: int (número natural)
        Descrição: Profundidade máxima de hierarquia para pré-contagem de pastas para determinar a porcentagem de progresso
        Uma hierarquia especificada maior pode levar mais tempo. Em vez disso, a precisão do progresso melhora.
        Valor esperado (3): NAS: 3~6, PC interno: 7~

    UsePhysicalSize: true
        Tipo: bool (true/false)
        Descrição: Se deve calcular o "tamanho alocado no disco" considerando o tamanho do cluster
        Valor esperado (true): Geralmente é recomendado manter true. Os resultados estarão mais próximos das exibições de propriedades do Windows. Se false, calcula por tamanho de arquivo.
        Antes de ajustar isso, recomendamos executar como administrador. Os arquivos do sistema serão incluídos nos cálculos para maior precisão.

    OldDataThresholdDays: 30
        Tipo: int (Inteiro não negativo)
        Descrição: Destaca a aba em amarelo para indicar dados de verificação antigos se o número de dias especificado tiver passado.
        Valor esperado: Preferência do usuário.

■ Como adicionar arquivos de idioma
--------------------
Esta ferramenta suporta vários idiomas e você pode adicionar novos.
1. Abra a pasta "Languages" na mesma hierarquia do executável do aplicativo (.exe).
2. Copie um arquivo existente como "en.yaml" e renomeie-o para o código de cultura do idioma que deseja adicionar (por exemplo, "fr.yaml" para francês).
   * Consulte o seguinte para uma lista de códigos de cultura (por exemplo: ja-JP / ja):
   https://learn.microsoft.com/pt-br/windows-hardware/manufacture/desktop/available-language-packs-for-windows?view=windows-11
3. Edite o texto dentro do arquivo YAML (salve no formato UTF-8).
4. Reinicie o aplicativo e o novo idioma aparecerá no menu "Language".
※ Se necessário, crie e adicione Readme_<language_code>.txt consultando outros arquivos.


■ Desinstalação completa (Excluir configurações e logs)
--------------------
Para remover completamente as configurações e logs de execução desta ferramenta, exclua manualmente a seguinte pasta:
%LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder
(Você pode abri-la diretamente colando o caminho acima na barra de endereços do Explorer)


■ Copyright
--------------------
Copyright (C) 2026 Ryoma Henzan / Cat & Chocolate Laboratory
