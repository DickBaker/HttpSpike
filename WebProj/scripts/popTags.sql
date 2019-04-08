insert into Tags(tag) values('a')
insert into Tags(tag) values('abbr')
insert into Tags(tag) values('acronym')
insert into Tags(tag) values('address')
insert into Tags(tag) values('applet')
insert into Tags(tag) values('area')
insert into Tags(tag) values('article')
insert into Tags(tag) values('aside')
insert into Tags(tag) values('audio')
insert into Tags(tag) values('b')
insert into Tags(tag) values('base')
insert into Tags(tag) values('basefont')
insert into Tags(tag) values('bdi')
insert into Tags(tag) values('bdo')
insert into Tags(tag) values('big')
insert into Tags(tag) values('blockquote')
insert into Tags(tag) values('body')
insert into Tags(tag) values('br')
insert into Tags(tag) values('button')
insert into Tags(tag) values('canvas')
insert into Tags(tag) values('caption')
insert into Tags(tag) values('center')
insert into Tags(tag) values('cite')
insert into Tags(tag) values('code')
insert into Tags(tag) values('col')
insert into Tags(tag) values('colgroup')
insert into Tags(tag) values('data')
insert into Tags(tag) values('datalist')
insert into Tags(tag) values('dd')
insert into Tags(tag) values('del')
insert into Tags(tag) values('details')
insert into Tags(tag) values('dfn')
insert into Tags(tag) values('dialog')
insert into Tags(tag) values('dir')
insert into Tags(tag) values('div')
insert into Tags(tag) values('dl')
insert into Tags(tag) values('dt')
insert into Tags(tag) values('em')
insert into Tags(tag) values('embed')
insert into Tags(tag) values('fieldset')
insert into Tags(tag) values('figcaption')
insert into Tags(tag) values('figure')
insert into Tags(tag) values('font')
insert into Tags(tag) values('footer')
insert into Tags(tag) values('form')
insert into Tags(tag) values('frame')
insert into Tags(tag) values('frameset')
insert into Tags(tag) values('hn')
insert into Tags(tag) values('head')
insert into Tags(tag) values('header')
insert into Tags(tag) values('hr')
insert into Tags(tag) values('html')
insert into Tags(tag) values('i')
insert into Tags(tag) values('iframe')
insert into Tags(tag) values('img')
insert into Tags(tag) values('input')
insert into Tags(tag) values('ins')
insert into Tags(tag) values('kbd')
insert into Tags(tag) values('label')
insert into Tags(tag) values('legend')
insert into Tags(tag) values('li')
insert into Tags(tag) values('link')
insert into Tags(tag) values('main')
insert into Tags(tag) values('map')
insert into Tags(tag) values('mark')
insert into Tags(tag) values('meta')
insert into Tags(tag) values('meter')
insert into Tags(tag) values('nav')
insert into Tags(tag) values('noframes')
insert into Tags(tag) values('noscript')
insert into Tags(tag) values('object')
insert into Tags(tag) values('ol')
insert into Tags(tag) values('optgroup')
insert into Tags(tag) values('option')
insert into Tags(tag) values('output')
insert into Tags(tag) values('p')
insert into Tags(tag) values('param')
insert into Tags(tag) values('picture')
insert into Tags(tag) values('pre')
insert into Tags(tag) values('progress')
insert into Tags(tag) values('q')
insert into Tags(tag) values('rp')
insert into Tags(tag) values('rt')
insert into Tags(tag) values('ruby')
insert into Tags(tag) values('s')
insert into Tags(tag) values('samp')
insert into Tags(tag) values('script')
insert into Tags(tag) values('section')
insert into Tags(tag) values('select')
insert into Tags(tag) values('small')
insert into Tags(tag) values('source')
insert into Tags(tag) values('span')
insert into Tags(tag) values('strike')
insert into Tags(tag) values('strong')
insert into Tags(tag) values('style')
insert into Tags(tag) values('sub')
insert into Tags(tag) values('summary')
insert into Tags(tag) values('sup')
insert into Tags(tag) values('svg')
insert into Tags(tag) values('table')
insert into Tags(tag) values('tbody')
insert into Tags(tag) values('td')
insert into Tags(tag) values('template')
insert into Tags(tag) values('textarea')
insert into Tags(tag) values('tfoot')
insert into Tags(tag) values('th')
insert into Tags(tag) values('thead')
insert into Tags(tag) values('time')
insert into Tags(tag) values('title')
insert into Tags(tag) values('tr')
insert into Tags(tag) values('track')
insert into Tags(tag) values('tt')
insert into Tags(tag) values('u')
insert into Tags(tag) values('ul')
insert into Tags(tag) values('var')
insert into Tags(tag) values('video')
insert into Tags(tag) values('wbr')
go

USE [Web]
GO

INSERT INTO dbo.HtmlAttribute (HtmlElemId, AttribName, Extn)
    select	E.HtmlElemId, 'href', 'html'
	from	dbo.Tags	E
	where	E.Tag='a'
INSERT INTO dbo.HtmlAttribute (HtmlElemId, AttribName, Extn)
    select	E.HtmlElemId, 'href', 'html'
	from	dbo.Tags	E
	where	E.Tag='area'
INSERT INTO dbo.HtmlAttribute (HtmlElemId, AttribName, Extn)
    select	E.HtmlElemId, 'src', 'mp4'
	from	dbo.Tags	E
	where	E.Tag='audio'
INSERT INTO dbo.HtmlAttribute (HtmlElemId, AttribName, Extn)
    select	E.HtmlElemId, 'href', 'html'
	from	dbo.Tags	E
	where	E.Tag='base'
INSERT INTO dbo.HtmlAttribute (HtmlElemId, AttribName, Extn)
    select	E.HtmlElemId, 'cite', 'html'
	from	dbo.Tags	E
	where	E.Tag='blockquote'
INSERT INTO dbo.HtmlAttribute (HtmlElemId, AttribName, Extn)
    select	E.HtmlElemId, 'cite', 'html'
	from	dbo.Tags	E
	where	E.Tag='del'
INSERT INTO dbo.HtmlAttribute (HtmlElemId, AttribName, Extn)
    select	E.HtmlElemId, 'src', 'html'
	from	dbo.Tags	E
	where	E.Tag='embed'
INSERT INTO dbo.HtmlAttribute (HtmlElemId, AttribName, Extn)
    select	E.HtmlElemId, 'src', 'html'
	from	dbo.Tags	E
	where	E.Tag='iframe'
INSERT INTO dbo.HtmlAttribute (HtmlElemId, AttribName, Extn)
    select	E.HtmlElemId, 'src', 'html'
	from	dbo.Tags	E
	where	E.Tag='img'
INSERT INTO dbo.HtmlAttribute (HtmlElemId, AttribName, Extn)
    select	E.HtmlElemId, 'src', 'html'
	from	dbo.Tags	E
	where	E.Tag='input'
INSERT INTO dbo.HtmlAttribute (HtmlElemId, AttribName, Extn)
    select	E.HtmlElemId, 'cite', 'html'
	from	dbo.Tags	E
	where	E.Tag='ins'
INSERT INTO dbo.HtmlAttribute (HtmlElemId, AttribName, Attrib1Name, Attrib1Value, Attrib2Name, Attrib2Value, Extn)
    select	E.HtmlElemId, 'href', 'rel','alternate', 'type', 'application/atom+xml', 'xml'
	from	dbo.Tags	E
	where	E.Tag='link'
INSERT INTO dbo.HtmlAttribute (HtmlElemId, AttribName, Attrib1Name, Attrib1Value, Attrib2Name, Attrib2Value, Extn)
    select	E.HtmlElemId, 'href', 'rel','icon', 'type', 'image/x-icon', 'ico'
	from	dbo.Tags	E
	where	E.Tag='link'
INSERT INTO dbo.HtmlAttribute (HtmlElemId, AttribName, Attrib1Name, Attrib1Value, Attrib2Name, Attrib2Value, Extn)
    select	E.HtmlElemId, 'href', 'rel','stylesheet', 'type', 'text/css', 'css'
	from	dbo.Tags	E
	where	E.Tag='link'
-- catch-all if specifics signatures above don't match
INSERT INTO dbo.HtmlAttribute (HtmlElemId, AttribName, Extn)
    select	E.HtmlElemId, 'href', 'html'
	from	dbo.Tags	E
	where	E.Tag='link'
INSERT INTO dbo.HtmlAttribute (HtmlElemId, AttribName, Extn)
    select	E.HtmlElemId, 'data', 'data'
	from	dbo.Tags	E
	where	E.Tag='object'
INSERT INTO dbo.HtmlAttribute (HtmlElemId, AttribName, Extn)
    select	E.HtmlElemId, 'cite', 'html'
	from	dbo.Tags	E
	where	E.Tag='q'
INSERT INTO dbo.HtmlAttribute (HtmlElemId, AttribName, Extn)
    select	E.HtmlElemId, 'src', 'js'
	from	dbo.Tags	E
	where	E.Tag='script'
INSERT INTO dbo.HtmlAttribute (HtmlElemId, AttribName, Extn)
    select	E.HtmlElemId, 'src', 'html'
	from	dbo.Tags	E
	where	E.Tag='source'
INSERT INTO dbo.HtmlAttribute (HtmlElemId, AttribName, Extn)
    select	E.HtmlElemId, 'srcset', 'html'
	from	dbo.Tags	E
	where	E.Tag='source'
INSERT INTO dbo.HtmlAttribute (HtmlElemId, AttribName, Extn)
    select	E.HtmlElemId, 'src', 'data'
	from	dbo.Tags	E
	where	E.Tag='track'
INSERT INTO dbo.HtmlAttribute (HtmlElemId, AttribName, Extn)
    select	E.HtmlElemId, 'src', 'mp4'
	from	dbo.Tags	E
	where	E.Tag='video'
GO
