#define WILL_NAVIGATE_BLOCKS

using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Dendrite.Framework;
using Dendrite.BusinessObject;
using Dendrite.CdlClasses;
using Dendrite.WebForce;
using Dendrite.IPhone.Forms;
using Dendrite.IPhone.Forms.Layout;
using Dendrite.WebForce.MobileComponents.Controls;


namespace MI.CustomControls
{
	public class MobileCustomerSearchControl : ICustomControlFactory
	{
		public System.ComponentModel.IComponent CreateWebInstance()
		{
			throw new System.NotImplementedException();
		}

		public System.ComponentModel.IComponent CreateWindowsInstance()
		{
			return new MobileCustomerSearchControlView();
		}
	}

	class StringHelper
	{
		IStringStoreEx _stringStore;
		WeakObject<CdlViewController> _controller;

		public StringHelper()
		{
			_stringStore = Configuration.Services[UserHelper.ConfigurationToken].StringStore as IStringStoreEx;
			_stringStore.Seed("wf_individuals");
			_stringStore.Seed("wf_organizations");
			_stringStore.Seed("wf_uncheck_all");
			_stringStore.Seed("select_all");
			_stringStore.Seed("wf_select_none");
		}

		public CdlViewController Controller
		{
			get
			{
				return WeakObject<CdlViewController>.Get(_controller);
			}
			set
			{
				_controller = WeakObject<CdlViewController>.Create(value);
			}
		}

		public void Detach()
		{
			ClearTimer();
			Controller = null;
		}

		public void Seed(string code)
		{
			_stringStore.Seed(code);
		}

		public string this[string code]
		{
			get
			{
				return _stringStore.GetString(UserHelper.ConfigurationToken, code);
			}
		}

		public IBusinessObject BusinessObject
		{
			get;
			set;
		}

		public bool NeedRefresh
		{
			get
			{
				if (BusinessObject == null)
					return false;

				int count = (int)Parameter.Get(BusinessObject.CreateParams.ExtendedProperties, "NEED-REFRESH", typeof(int), 0);
				return count > 0;
			}
			set
			{
				if (BusinessObject != null)
					BusinessObject.CreateParams.ExtendedProperties["NEED-REFRESH"] = value ? "1" : "0";
			}
		}

        IDisposable _timer = null;
		bool _timerPaused = false;

#if DEBUG
		const double _timerDelay = 3;
#else
        const double _timerDelay = 1;
#endif

		public void ClearTimer(bool pause = false)
		{
			lock (this)
			{
				AppDelegate.Instance.WillNavigate -= HandleWillNavigate;
				NeedRefresh = false;

				if (_timer != null)
				{
					_timer?.Dispose();
					_timer = null;
					_timerPaused = pause;
				}
			}
		}

		public void CreateTimer()
		{
			lock (this)
			{
				ClearTimer();

				NeedRefresh = true;
				_timer = AppDelegate.Schedule(_timerDelay, () =>
				{
					RefreshCustomerSelection();
				});
				AppDelegate.Instance.WillNavigate += HandleWillNavigate;
			}
		}

		void HandleWillNavigate(object sender, CancelEventArgs e)
		{
			e.Cancel = true; // Prevents Navigation while timer is active 
		}

		void RefreshCustomerSelection(bool force = false)
		{
			if (AppDelegate.Instance.ApplicationBusy)
				return;

			try
			{
				AppDelegate.Instance.StartShield();

				lock (this)
				{
					if (!force && _timer == null)
					{
						AppDelegate.Instance.WillNavigate -= HandleWillNavigate;
						_timerPaused = false;
						NeedRefresh = false;
					}
					else if (NeedRefresh)
					{
						ClearTimer();
						string filter = MobileCustomerSearchHelper.CurrentFilter;

						if (!force)
						{
							if (string.Compare(MobileCustomerSearchHelper.OriginalFilter, filter, StringComparison.OrdinalIgnoreCase) == 0)
								return;
						}

						MobileCustomerSearchHelper.OriginalFilter = filter;

						string fromModule = Dendrite.Framework.Parameter.GetAsString(BusinessObject.CreateParams.ExtendedProperties, "FROM");
						NameValueCollection query = new NameValueCollection();
						query["CUSTTYPE-FILTER"] = filter;
						query["NO-DEFAULT-FILTER"] = "1";
						query["NEED-REFRESH"] = "0";
						query["FROM"] = fromModule;
						/*
          Is Lucene Enabled
         0 : No
         - 1 : Both online and offline
         -2: Online only
         -3: Offline only
         */
						var useLuceneSearch = false;
						var luceneParameter = ParameterHelper.GetStringValue(BusinessObject, "general mi touch", "Use Lucene Search");
						var isOffline = (bool)EnvironmentVariables.Instance.GetValue(BusinessObject.CreateParams.UserToken, "@OFFLINE");
						if (isOffline && (luceneParameter == "-1" || luceneParameter == "-3"))
							useLuceneSearch = true;
						else if (!isOffline && (luceneParameter == "-1" || luceneParameter == "-2"))
							useLuceneSearch = true;
						else
							useLuceneSearch = false;
						if (useLuceneSearch)
							AppDelegate.Instance.Navigate("Targeting", "mobile", "customerSearchLucene.cdl", "_parent", query);
						else
							AppDelegate.Instance.Navigate("Targeting", "mobile", "customerSearch.cdl", "_parent", query);
					}
					else
						ClearTimer();
				}
			}
			finally
			{
				AppDelegate.Instance.EndShield();
			}
		}
	}

    class MobileCustomerSearchControlView : DrteLinearLayout, ICdlIPhoneDataControl, ICdlDataItemHandler
	{
		bool _isControlLoaded = false;
		StringHelper _stringHelper = new StringHelper();
		MobileCustomerSearchHelper _helper = new MobileCustomerSearchHelper();
		bool _isPresetList = false;

		const float _rowHeight = 41f;

        public MobileCustomerSearchControlView()
        {
            Orientation = Android.Widget.Orientation.Vertical;
        }

		public MobileCustomerSearchControlView(IntPtr handle, Android.Runtime.JniHandleOwnership transfer) : base(handle, transfer)
		{
			DebugLog.Trace("MobileCustomerSearchControlView(IntPtr) called!!!");
		}

		#region ICdlDataItemHandler implementation
		public ICdlDataItem DataItem
		{
			get;
			set;
		}
		#endregion

		/// <summary>
		/// Indicates whether the containing page is displaying a preset list.  If so, no I elements should be rendered, but the
		/// code for setting up the customer type filter still gets applied.
		/// </summary>
		/// <value>True or False</value>
		[Assignable(true)]
		public bool PresetList
		{
			get { return _isPresetList; }
			set { _isPresetList = value; }
		}

		public void Databind()
		{
			if (_isPresetList)
				return;

			if (_isControlLoaded)
				return;
			_isControlLoaded = true;

			if (CdlSettings.Organizer != null)
			{
				_stringHelper.BusinessObject = CdlSettings.Organizer.BusinessObject;
				_stringHelper.Controller = CdlSettings.Organizer.Form as CdlViewController;
			}

			CustomerSearchTypeView view;
			float y = 0f;

			_helper.SetDisplayTypes();
			view = new CustomerSearchTypeView(_stringHelper, true, _stringHelper["wf_individuals"], "INDV", string.Empty);
			this.AddView(view);
			y += 36f;

			foreach (string customerType in _helper.DisplayPersonTypes.Keys)
				AddCustomerType(customerType, _helper.DisplayPersonTypes[customerType].ToString(), ref y);

			view = new CustomerSearchTypeView(_stringHelper, true, _stringHelper["wf_organizations"], "ORG", string.Empty);
			this.AddView(view);
			y += 36f;

			foreach (string customerType in _helper.DisplayOrgTypes.Keys)
				AddCustomerType(customerType, _helper.DisplayOrgTypes[customerType].ToString(), ref y);
		}

		private void AddCustomerType(string customerType, string label, ref float y)
		{
			var view = new CustomerSearchTypeView(_stringHelper, false, label, customerType, CustomerInfo.GetImage(customerType));
			this.AddView(view);
			y += _rowHeight;
		}
	}

    class CustomerSearchTypeView : DrteLinearLayout, ICdlIPhoneDataControl, ICdlDataItemHandler
	{
		StringHelper _stringHelper;
        Android.Widget.TextView _titleLabelView = null;
		Android.Widget.ImageView _custTypeImageView = null;
		Android.Widget.TextView _custTypeLabelView = null;
		MobileCustomerSearchHelper _helper = new MobileCustomerSearchHelper();
		bool _isControlLoaded = false;
		string _original;
		CdlCheckBoxFactory.MultiCheckBox _selectCust;
		SelectAllButton _selectAllButton;

		const float _padding = 7f;//5f;
		const float _spacing = 5f;

		public CustomerSearchTypeView(StringHelper stringHelper, bool titleMode, string label, string type, string image)
		{
            Orientation = Android.Widget.Orientation.Horizontal;

			_stringHelper = stringHelper;
			_helper = new MobileCustomerSearchHelper();
			TitleMode = titleMode;
			Label = label;
			CustomerType = type;
			Image = image;

			this.SetWillNotDraw(false);

            this.SetAccessibilityIdentifier(string.Format("TYPE:{0}", CustomerType));

			if (TitleMode)
			{
                this.SetBackgroundColor(StyleGuide.OpaqueSkinColor);
				this.TitleLabel = Label;
				_selectAllButton = new SelectAllButton(stringHelper, _helper);
                _selectAllButton.Click += Handle_selectAllButtonTouchDown;
				
                var layoutParameters = new Android.Widget.LinearLayout.LayoutParams(
                    Android.Views.ViewGroup.LayoutParams.WrapContent, 
                    Android.Views.ViewGroup.LayoutParams.WrapContent);
				layoutParameters.Gravity = Android.Views.GravityFlags.Center;
				_selectAllButton.LayoutParameters = layoutParameters;
				
                this.AddView(_selectAllButton);
				//this.SizeToFit();
			}
			else
			{
				this.CustImage = ImageUrlHelper.GetImage(Image);
				this.CustTypeLabel = Label; ;
				_selectCust = new CdlCheckBoxFactory.MultiCheckBox(false);
				_selectCust.On = false;
				_selectCust.On = _helper.IsSelected;
				_selectCust.ValueChanged += Handle_selectCustValueChanged;

				var layoutParameters = new Android.Widget.LinearLayout.LayoutParams(
                    Android.Views.ViewGroup.LayoutParams.WrapContent, 
                    Android.Views.ViewGroup.LayoutParams.WrapContent);
                layoutParameters.SetMargins(8.px(), 8.px(), 8.px(), 8.px());
                layoutParameters.Gravity = Android.Views.GravityFlags.Center;
                _selectCust.LayoutParameters = layoutParameters;

				this.AddView(_selectCust);
				//this.SizeToFit();
			}
		}

		public CustomerSearchTypeView(IntPtr handle, Android.Runtime.JniHandleOwnership transfer) : base(handle, transfer)
		{
			DebugLog.Trace("CustomerSearchTypeView(IntPtr) called!!!");
		}

		public void Update()
		{
			if (TitleMode)
			{
				_selectAllButton.Update();
			}
			else
			{
				_selectCust.On = _helper.IsSelected;
			}
		}

		public override void Draw(Android.Graphics.Canvas canvas)
		{
			base.Draw(canvas);

			if (!TitleMode)
			{
				var rect = canvas.ClipBounds;
				int size = StyleGuide.MCSC_SortOptTbl_LayerBorderWidth.px();
				int x = rect.Width();

				var paint = new Android.Graphics.Paint();
				paint.Color = MI.StyleGuide.MCSC_SortOptTbl_BorderColor;
				paint.StrokeWidth = size;
				paint.SetStyle(Android.Graphics.Paint.Style.Stroke);

				canvas.DrawLine(0f, 0f, x, 0f, paint);
			}
		}

		#region ICdlDataItemHandler implementation
		public ICdlDataItem DataItem
		{
			get;
			set;
		}
		#endregion

		#region Custom Properties

		/// <summary>
		/// Gets or sets the customer type image
		/// </summary>
		/// <value>The control properties.</value>
		[Assignable(true)]
		public string Image
		{
			get;
			set;
		}

		/// <summary>
		/// Gets or sets the customer type label
		/// </summary>
		/// <value>The control properties.</value>
		[Assignable(true)]
		public string Label
		{
			get;
			set;
		}

		/// <summary>
		/// Title Mode  
		/// </summary>
		[Assignable(true)]
		public bool TitleMode
		{
			get;
			set;
		}

		/// <summary>
		/// Gets or sets the customer type 
		/// </summary>
		/// <value>The control properties.</value>
		[Assignable(true)]
		public string CustomerType
		{
			get { return _helper.CustomerType; }
			set { _helper.CustomerType = value; }
		}

		#endregion Custom Properties

		public SelectAllButton GetSelectAllButton()
		{
			return _selectAllButton;
		}

		public void Databind()
		{
		}

		void UpdateAll()
		{
			var parent = this.Parent as Android.Views.ViewGroup;
			if (parent != null)
			{
				var count = parent.ChildCount;
				for (var i = 0; i < count; i++)
				{
					var customerSearchTypeView = parent.GetChildAt(i) as CustomerSearchTypeView;
					customerSearchTypeView?.Update();
				}
			}
			_stringHelper.CreateTimer();
		}

		void Handle_selectAllButtonTouchDown(object sender, EventArgs e)
		{
			bool modified = _helper.SelectAll();

            if (modified)
                UpdateAll();
		}

		void Handle_selectCustValueChanged(object sender, EventArgs e)
		{
			bool modified;
			if (_selectCust.On)
				modified = _helper.AddCustomerType();
			else
				modified = _helper.RemoveCustomerType();

			if (modified)
				UpdateAll();
		}

		protected override ILayoutEngine CreateLayoutEngine()
		{
			return null;
		}

		protected Android.Graphics.Drawables.Drawable CustImage
		{
			get
			{
				if (_custTypeImageView != null)
                    return _custTypeImageView.Drawable;
				else
					return null;
			}
			set
			{
				if (_custTypeImageView != null)
				{
					if (value == null)
					{
                        _custTypeImageView.RemoveFromSuperview();
						_custTypeImageView = null;
					}
					else
					{
                        _custTypeImageView.SetImageDrawable(value);
					}
				}
				else if (value != null)
				{
                    _custTypeImageView = new Android.Widget.ImageView(Context);
					_custTypeImageView.SetImageDrawable(value);
					
                    var layoutParameters = new Android.Widget.LinearLayout.LayoutParams(
                        Android.Views.ViewGroup.LayoutParams.WrapContent,
                        Android.Views.ViewGroup.LayoutParams.WrapContent);
					layoutParameters.SetMargins(8.px(), 8.px(), 8.px(), 8.px());
					layoutParameters.Gravity = Android.Views.GravityFlags.Center;
					_custTypeImageView.LayoutParameters = layoutParameters;

					this.AddView(_custTypeImageView);
				}
			}
		}

		protected string CustTypeLabel
		{
			get
			{
				if (_custTypeLabelView != null)
					return _custTypeLabelView.Text;
				else
					return null;
			}
			set
			{
				if (_custTypeLabelView != null)
				{
					if (value == null || value.Length == 0)
					{
                        _custTypeLabelView.RemoveFromSuperview();
						_custTypeLabelView = null;
					}
					else
					{
						_custTypeLabelView.Text = value;
					}
				}
				else if (value != null)
				{
					_custTypeLabelView = new DetachableLabel();
					_custTypeLabelView.Text = value;
                    _custTypeLabelView.SetTextColor(StyleGuide.MCSC_Default_LabelTextColor);
                    _custTypeLabelView.SetBackgroundColor(StyleGuide.MCSC_Default_LabelBgColor);
                    _custTypeLabelView.SetFont(StyleGuide.MCSC_Default_LabelFont);

					var layoutParameters = new Android.Widget.LinearLayout.LayoutParams(
                        Android.Views.ViewGroup.LayoutParams.WrapContent, 
                        Android.Views.ViewGroup.LayoutParams.WrapContent, 
                        1f);    // Weight
					layoutParameters.SetMargins(8.px(), 8.px(), 8.px(), 8.px());
					layoutParameters.Gravity = Android.Views.GravityFlags.Center;
					_custTypeLabelView.LayoutParameters = layoutParameters;

					_custTypeLabelView.SetMaxLines(1);
					_custTypeLabelView.Ellipsize = Android.Text.TextUtils.TruncateAt.End;

					this.AddView(_custTypeLabelView);
				}
			}
		}

		protected string TitleLabel
		{
			get
			{
				if (_titleLabelView != null)
					return _titleLabelView.Text;
				else
					return null;
			}
			set
			{
				if (_titleLabelView != null)
				{
					if (value == null || value.Length == 0)
					{
						_titleLabelView.RemoveFromSuperview();
						_titleLabelView = null;
					}
					else
					{
						_titleLabelView.Text = value;
					}
				}
				else if (value != null)
				{
					_titleLabelView = new DetachableLabel();
					_titleLabelView.Text = value;
                    _titleLabelView.SetTextColor(StyleGuide.MCSC_Default_LabelTextColor);
                    _titleLabelView.SetBackgroundColor(StyleGuide.MCSC_Default_LabelBgColor);
                    _titleLabelView.SetFont(StyleGuide.MCSC_Default_LabelFont);

					var layoutParameters = new Android.Widget.LinearLayout.LayoutParams(Android.Views.ViewGroup.LayoutParams.WrapContent, Android.Views.ViewGroup.LayoutParams.WrapContent, 1f);
					layoutParameters.SetMargins(8.px(), 8.px(), 8.px(), 8.px());
					layoutParameters.Gravity = Android.Views.GravityFlags.Center;
					_titleLabelView.LayoutParameters = layoutParameters;

                    this.AddView(_titleLabelView);
				}
			}
		}
	}

	class SelectAllButton : DetachableButton
	{
        StringHelper StringHelper { get; set; }
        MobileCustomerSearchHelper Helper { get; set; }

        internal SelectAllButton(StringHelper stringHelper, MobileCustomerSearchHelper helper) : base()
		{
            StringHelper = stringHelper;
            Helper = helper;

			this.SetBackgroundColor(Android.Graphics.Color.Transparent);
			this.SetTextColor(StyleGuide.MCSC_Default_LabelTextColor);
			this.SetFont(StyleGuide.MCSC_Default_LabelFont);

            Update();
		}

		public SelectAllButton(IntPtr handle, Android.Runtime.JniHandleOwnership transfer) : base(handle, transfer)
		{
			DebugLog.Trace("SelectAllButton(IntPtr) called!!!");
		}

		void HandleTouchDown(object sender, EventArgs e)
		{
		}

		void SetState()
		{
		}

        public void Update()
        {
			string title;
			if (Helper.AllSelected())
				title = StringHelper["wf_select_none"];
			else
				title = StringHelper["select_all"];

			Text = title;
        }
	}
}
