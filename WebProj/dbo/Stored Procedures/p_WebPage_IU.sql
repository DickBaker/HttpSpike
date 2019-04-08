
CREATE PROCEDURE dbo.p_WebPage_IU 
/*
PURPOSE
	upsert WebPage row

HISTORY
	20190313 dbaker created
*/
(	@Url			nvarchar(450),
	@DraftFilespec	nvarchar(511)	= NULL,
	@Filespec		nvarchar(511)	= NULL,
	@NeedDownload	bit				= 0
	)
AS
BEGIN
	SET NOCOUNT ON;	-- prevent extra result sets from interfering with SELECT statements

    declare @XPageId		int
		,	@XHostId		int
		,	@XUrl			nvarchar(450)
		,	@XDraftFilespec	nvarchar(511)
		,	@XFilespec		nvarchar(511)
		,	@XNeedDownload	bit
	select	@XPageId, @XHostId, @XUrl, @XDraftFilespec, @XFilespec, @XNeedDownload
	from	dbo.WebPages
	where	[Url]	= @Url
	if @XPageId is null
	  begin
	  	INSERT INTO dbo.WebPages
		  (	--	HostId
				[Url]
			,	DraftFilespec
			,	Filespec
			,	NeedDownload
		  )
		VALUES
		 (	--	@HostId,
				@Url
			,	@DraftFilespec
			,	@Filespec
			,	@NeedDownload
		  )
		set @XPageId = SCOPE_IDENTITY()
	  end
	 else
		UPDATE dbo.WebPages SET
			--	[Url]			= coalesce(@Url,			@Url)
				DraftFilespec	= coalesce(@DraftFilespec,	@DraftFilespec)
			,	[Filespec]		= coalesce(@Filespec,		@Filespec)
			,	NeedDownload	= coalesce(@NeedDownload,	@NeedDownload)
		WHERE	PageId	=	@XPageId

	select	PageId, HostId, Url, DraftFilespec, Filespec, NeedDownload
	from	dbo.WebPages
	where	PageId = @XPageId
END