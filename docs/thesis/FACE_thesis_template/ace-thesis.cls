%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
% Author: M.Marian 				                                        %
% Date: 2026.01.19 - first version                                      %
%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
\NeedsTeXFormat{LaTeX2e}
\ProvidesClass{ace-thesis}
\DeclareOption*{\PassOptionsToClass{\CurrentOption}{report}}
\ProcessOptions
\LoadClass{report}

%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
% Packages used for this template                                       %
%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
\usepackage{booktabs}
\usepackage{makecell}
\usepackage{amsmath}
\usepackage{pdfpages}
\usepackage{export}
\usepackage{hyperref}
\usepackage{caption}
\usepackage{doi}
\usepackage{imakeidx}
\usepackage[table]{xcolor}
\usepackage{array}
\usepackage{multicol}
\usepackage[euler]{textgreek}
\usepackage{lipsum} 
\usepackage{mathtools}
\usepackage{multirow}
\usepackage{lscape}
\usepackage{subfigure}
\usepackage{caption}
\usepackage[chapter]{algorithm}
\usepackage{algorithmicx}
\usepackage{algpseudocode}
\usepackage{longtable}
\usepackage{lipsum}
\usepackage{fancyhdr}
% Font Settings
% 8-bits encoding, many text sym­bols (such as baht, bul­let, copy­right, mu­si­cal­note, onequar­ter, sec­tion, and yen), Latin Modern fonts
\usepackage[T1]{fontenc}
\usepackage{textcomp}
\usepackage{lmodern}

% UTF-8 input encoding for Romanian fonts
\usepackage{amsfonts}
\usepackage[utf8x]{inputenc}
\usepackage[english, romanian]{babel}

% Set paper size and margins
\usepackage[a4paper, vmargin=2.0cm]{geometry}

\setlength{\textheight}{24cm}
\setlength{\textwidth}{15cm}
\setlength{\topmargin}{-0.9cm}
\setlength{\headheight}{0.6cm}
\setlength{\headsep}{1cm}
\setlength{\footskip}{1cm}
\setlength{\oddsidemargin}{5mm}
\setlength{\evensidemargin}{5mm}
\setlength{\marginparwidth}{2cm}

\usepackage{marginnote}

% Paragraph and line properties
\setlength{\parindent}{0.8cm}
\setlength{\parskip}{0.5pt}
\linespread{1.2}
\sloppy % reduce number of word divisions and use more space between words

\usepackage{enumitem,setspace}
\setlist[itemize]{noitemsep, topsep=0pt, after=\vspace{3pt}}

%% Chapter style
\usepackage{titlesec, blindtext, color}
\usepackage{sectsty}
\chapterfont{\raggedright}
\allsectionsfont{\raggedright}

\newcommand{\hsp}{\hspace{20pt}}
\titleformat{\chapter}[hang]{\huge\bfseries}{\thechapter\hsp\textcolor{gray}{|}\hsp}{0pt}{\huge\bfseries}

% DEDICATION. The dedication environment makes sure the dedication gets its
% own page and is set out in verse format. Example:
% \begin{dedication} 
	% Include text here
	% \end{dedication}
\newenvironment{dedication}
{\newpage\thispagestyle{empty}\vspace*{5cm}\begin{quotation}\begin{center}\begin{em}}
			{\par\end{em}\end{center}\end{quotation}\newpage}

% Insert a motto. Example:
% \begin{chapquote}{Lewis Carroll, \textit{Alice in Wonderland}}
	% ``Begin at the beginning,'' the King said, gravely, ``and go on till you
	% come to an end; then stop.''
	% \end{chapquote}
\makeatletter
\renewcommand{\@chapapp}{}% Not necessary...
\newenvironment{chapquote}[2][2em]
{\setlength{\@tempdima}{#1}%
	\def\chapquote@author{#2}%
	\parshape 1 \@tempdima \dimexpr\textwidth-2\@tempdima\relax%
	\itshape}
{\par\normalfont\hfill--\ \chapquote@author\hspace*{\@tempdima}\par\bigskip}
\makeatother

% Institution names
\newcommand{\ACEUniversity}[1]{\textsc{\huge #1}}
\newcommand{\ACEFaculty}[1]{\textsc{\large #1}}
\newcommand{\ACEDepartment}[1]{\textsc{\large #1}}
% Thesis details
\newcommand{\ACEThesisType}[1]{\textsc{\Huge #1}}
\newcommand{\ACEThesisDomain}[1]{\textsc{\large #1}}
% Titles, authors, supervisors
\newcommand{\ACEThesisTitle}[1]{\textbf{\Large #1}}
\newcommand{\ACEThesisTitleAlt}[1]{\textit{\large #1}}
\newcommand{\ACEThesisAuthor}[1]{\textsc{\Large #1}}
\newcommand{\ACEThesisSupervisor}[1]{\textsc{\Large #1}}


\newcolumntype{L}[1]{>{\raggedright\let\newline\\\arraybackslash\hspace{0pt}}m{#1}}
\newcolumntype{C}[1]{>{\centering\let\newline\\\arraybackslash\hspace{0pt}}m{#1}}
\newcolumntype{R}[1]{>{\raggedleft\let\newline\\\arraybackslash\hspace{0pt}}m{#1}}


\newcommand{\checkedBox}{\mbox{\ooalign{$\checkmark$\cr\hidewidth$\square$\hidewidth\cr}}}
\newcommand{\unCheckedBox}{\mbox{\ooalign{\cr\hidewidth$\square$\hidewidth\cr}}}

\hypersetup{
	colorlinks = true,
	linkcolor=black,   % color of internal links
	citecolor=blue,   % color of links to bibliography
	urlcolor=blue,    % color of external links
	%pagebackref=true,
	%implicit=false,
	%bookmarks=true,
	bookmarksopen=true,
	pdfdisplaydoctitle=true
}