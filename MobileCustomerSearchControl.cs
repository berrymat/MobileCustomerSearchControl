#define WILL_NAVIGATE_BLOCKS

using System;
using System.Collections;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
//using System.Drawing ;
using System.Web;
using System.ComponentModel;
using Dendrite.Framework;
using Dendrite.BusinessObject;
using Dendrite.CdlClasses;
using Dendrite.CdlControls;
using Dendrite.WebFramework;
using Dendrite.WebForce;
using Dendrite.IPhone.Forms;
using Dendrite.IPhone.Forms.Layout;
using Foundation;
using UIKit;
using CoreGraphics;
using CoreAnimation;
using System.Text;
using Dendrite.WebForce.MobileComponents.Controls;


namespace MI.CustomControls
{
	[Foundation.Preserve(AllMembers = true)]
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
				var old = Controller;

				if (old != null)
					old.DidRestoreState -= HandleDidRestoreState;

				_controller = WeakObject<CdlViewController>.Create(value);

				if (value != null)
					value.DidRestoreState += HandleDidRestoreState;
			}
		}

		void HandleDidRestoreState(object sender, EventArgs e)
		{
#if !WILL_NAVIGATE_BLOCKS
            if (_timerPaused)
            {
                if (AppDelegate.Instance.ApplicationBusy)
                {
                    EventHandler        busyChanged = null;
                    CancelEventHandler  willNavigate = null;
                    
                    busyChanged = (a, b) => 
                    {
                        AppDelegate.Instance.ApplicationBusyChanged -= busyChanged;
                        AppDelegate.Instance.WillNavigate           -= willNavigate;
                        RefreshCustomerSelection(true);
                    };
                    
                    willNavigate = (c, d) =>
                    {
                        AppDelegate.Instance.ApplicationBusyChanged -= busyChanged;
                        AppDelegate.Instance.WillNavigate           -= willNavigate;
                    };
                    
                    AppDelegate.Instance.ApplicationBusyChanged += busyChanged;
                    AppDelegate.Instance.WillNavigate           += willNavigate;
                }
                else
                    RefreshCustomerSelection(true);
            }
#endif
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

		NSTimer _timer = null;
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
					if (_timer.IsValid)
						_timer.Invalidate();

					_timer.Dispose();
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
				_timer = NSTimer.CreateScheduledTimer(_timerDelay, (t) =>
				{
					RefreshCustomerSelection();
				});
				AppDelegate.Instance.WillNavigate += HandleWillNavigate;
			}
		}

		[System.Runtime.InteropServices.DllImport(ObjCRuntime.Constants.AudioToolboxLibrary)]
		static extern void AudioServicesPlaySystemSound(uint inSystemSoundID);

		void HandleWillNavigate(object sender, CancelEventArgs e)
		{
#if WILL_NAVIGATE_BLOCKS
			e.Cancel = true; // Prevents Navigation while timer is active 
			AudioServicesPlaySystemSound(1053);
#else
            ClearTimer(true);
#endif
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
					if (!force && (_timer == null || !_timer.IsValid))
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

	[Foundation.Preserve(AllMembers = true)]
	class MobileCustomerSearchControlView : DrteView, ICdlIPhoneDataControl, ICdlDataItemHandler
	{
		bool _isControlLoaded = false;
		StringHelper _stringHelper = new StringHelper();
		MobileCustomerSearchHelper _helper = new MobileCustomerSearchHelper();
		bool _isPresetList = false;

		const float _rowHeight = 41f;

		public MobileCustomerSearchControlView()
		{
		}

		public MobileCustomerSearchControlView(IntPtr handle) : base(handle)
		{
			DebugLog.Trace("MobileCustomerSearchControlView(IntPtr) called!!!");
		}

		public override void Detach()
		{
			if (_stringHelper != null)
				_stringHelper.Detach();
			base.Detach();
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
			view.Frame = new CGRect(new CGPoint(0, y), new CGSize(300f, 36f));
			view.UserInteractionEnabled = true;
			this.AddSubview(view);
			y += 36f;

			foreach (string customerType in _helper.DisplayPersonTypes.Keys)
				AddCustomerType(customerType, _helper.DisplayPersonTypes[customerType].ToString(), ref y);

			view = new CustomerSearchTypeView(_stringHelper, true, _stringHelper["wf_organizations"], "ORG", string.Empty);
			view.Frame = new CGRect(new CGPoint(0, y), new CGSize(300f, 36f));
			view.UserInteractionEnabled = true;
			this.AddSubview(view);
			y += 36f;

			foreach (string customerType in _helper.DisplayOrgTypes.Keys)
				AddCustomerType(customerType, _helper.DisplayOrgTypes[customerType].ToString(), ref y);

			this.Frame = new CGRect(new CGPoint(0, 0), new CGSize(300f, y));
			this.SetNeedsLayout();
			this.SetNeedsDisplay();
		}

		private void AddCustomerType(string customerType, string label, ref float y)
		{
			var view = new CustomerSearchTypeView(_stringHelper, false, label, customerType, CustomerInfo.GetImage(customerType));
			view.Frame = new CGRect(new CGPoint(0, y), new CGSize(300f, _rowHeight));
			view.UserInteractionEnabled = true;
			this.AddSubview(view);
			y += _rowHeight;
		}

		protected override ILayoutEngine CreateLayoutEngine()
		{
			return null;
		}
	}

	[Foundation.Preserve(AllMembers = true)]
	class CustomerSearchTypeView : DrteView, ICdlIPhoneDataControl, ICdlDataItemHandler
	{
		StringHelper _stringHelper;
		UILabel _titleLabelView = null;
		UIImageView _custTypeImageView = null;
		UILabel _custTypeLabelView = null;
		MobileCustomerSearchHelper _helper = new MobileCustomerSearchHelper();
		bool _isControlLoaded = false;
		string _original;
		CdlCheckBoxFactory.MultiCheckBox _selectCust;
		SelectAllButton _selectAllButton;

		const float _padding = 7f;//5f;
		const float _spacing = 5f;

		public CustomerSearchTypeView(StringHelper stringHelper, bool titleMode, string label, string type, string image)
		{
			_stringHelper = stringHelper;
			_helper = new MobileCustomerSearchHelper();
			TitleMode = titleMode;
			Label = label;
			CustomerType = type;
			Image = image;

			this.SetAccessibilityIdentifier(string.Format("TYPE:{0}", CustomerType));

			if (TitleMode)
			{
				this.BackgroundColor = StyleGuide.OpaqueSkinColor;
				this.TitleLabel = Label;
				_selectAllButton = new SelectAllButton(stringHelper, _helper);
				_selectAllButton.TouchDown += Handle_selectAllButtonTouchDown;
				this.AddSubview(_selectAllButton);
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
				this.AddSubview(_selectCust);
				//this.SizeToFit();
			}
		}

		public CustomerSearchTypeView(IntPtr handle) : base(handle)
		{
			DebugLog.Trace("CustomerSearchTypeView(IntPtr) called!!!");
		}

		public override void Draw(CGRect rect)
		{
			base.Draw(rect);

			nfloat size = StyleGuide.MCSC_SortOptTbl_LayerBorderWidth;
			nfloat y = rect.Height - size + 1f;
			nfloat x = rect.Width;

			using (CGContext context = UIGraphics.GetCurrentContext())
			{
				if (context != null)
				{
					context.SaveState();
					context.SetShouldAntialias(false);
					context.SetStrokeColor(MI.StyleGuide.MCSC_SortOptTbl_BorderColor.CGColor);
					//if(TitleMode)
					context.SetLineWidth(size);

					context.AddLines(new CGPoint[]
						{
							new CGPoint(0f, y),
							new CGPoint(x, y)
						});

					context.DrawPath(CGPathDrawingMode.Stroke);
					context.RestoreState();
				}
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

		void Handle_selectAllButtonTouchDown(object sender, EventArgs e)
		{
			bool modified = _helper.SelectAll();

			if (modified)
				_stringHelper.CreateTimer();
		}

		void Handle_selectCustValueChanged(object sender, EventArgs e)
		{
			bool modified;
			if (_selectCust.On)
				modified = _helper.AddCustomerType();
			else
				modified = _helper.RemoveCustomerType();

			if (modified)
				_stringHelper.CreateTimer();
		}

		protected override ILayoutEngine CreateLayoutEngine()
		{
			return null;
		}

		protected UIImage CustImage
		{
			get
			{
				if (_custTypeImageView != null)
					return _custTypeImageView.Image;
				else
					return null;
			}
			set
			{
				if (_custTypeImageView != null)
				{
					if (value == null)
					{
						_custTypeImageView.DetachView();
						_custTypeImageView = null;
						this.SetNeedsLayout();
					}
					else
					{
						_custTypeImageView.Image = value;
						this.SetNeedsLayout();
					}
				}
				else if (value != null)
				{
					_custTypeImageView = new DetachableImageView(value);
					this.AddSubview(_custTypeImageView);
					this.SetNeedsLayout();
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
						_custTypeLabelView.DetachView();
						_custTypeLabelView = null;
						this.SetNeedsLayout();
					}
					else
					{
						_custTypeLabelView.Text = value;
						this.SetNeedsLayout();
					}
				}
				else if (value != null)
				{
					_custTypeLabelView = new DetachableLabel();
					_custTypeLabelView.Text = value;
					_custTypeLabelView.TextColor = StyleGuide.MCSC_Default_LabelTextColor;
					_custTypeLabelView.BackgroundColor = StyleGuide.MCSC_Default_LabelBgColor;
					_custTypeLabelView.Font = StyleGuide.MCSC_Default_LabelFont;
					this.AddSubview(_custTypeLabelView);
					this.SetNeedsLayout();
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
						_titleLabelView.DetachView();
						_titleLabelView = null;
						this.SetNeedsLayout();
					}
					else
					{
						_titleLabelView.Text = value;
						this.SetNeedsLayout();
					}
				}
				else if (value != null)
				{
					_titleLabelView = new DetachableLabel();
					_titleLabelView.Text = value;
					_titleLabelView.TextColor = StyleGuide.MCSC_Default_LabelTextColor;
					_titleLabelView.BackgroundColor = StyleGuide.MCSC_Default_LabelBgColor;
					_titleLabelView.Font = StyleGuide.MCSC_Default_LabelFont;
					this.AddSubview(_titleLabelView);
					this.SetNeedsLayout();
				}
			}
		}

		CGSize TitleModeSizing(CGSize bounds, bool layout)
		{
			CGSize size1 = CGSize.Empty;
			CGSize size2 = CGSize.Empty;

			if (_titleLabelView != null)
				size1 = _titleLabelView.SizeThatFits(bounds);

			if (_selectAllButton != null)
				size2 = _selectAllButton.SizeThatFits(bounds);


			if (layout)
			{
				if (_titleLabelView != null)
				{
					var offset = (bounds.Height - size1.Height) / 2;
					_titleLabelView.Frame = new CGRect(new CGPoint(_padding, offset), size1);
				}

				if (_selectAllButton != null)
				{
					var offset = (bounds.Height - size2.Height) / 2;
					var x = bounds.Width - size2.Width;

					_selectAllButton.Frame = new CGRect(new CGPoint(x, offset), size2);
				}
			}

			nfloat height = NMath.Max(size2.Height, size1.Height);
			return new CGSize(bounds.Width, height + _padding + _padding);
		}

		CGSize NormalModeSizing(CGSize bounds, bool layout)
		{
			CGSize size1 = CGSize.Empty;
			CGSize size2 = CGSize.Empty;
			CGSize size3 = CGSize.Empty;

			if (_custTypeImageView != null)
				size1 = _custTypeImageView.SizeThatFits(bounds);

			if (_selectCust != null)
				size3 = _selectCust.SizeThatFits(bounds);

			nfloat remainingWidth = bounds.Width - size1.Width - _spacing - _spacing - size3.Width;
			CGSize bounds2 = new CGSize(remainingWidth, bounds.Height);

			if (_custTypeLabelView != null)
				size2 = _custTypeLabelView.SizeThatFits(bounds2);

			if (layout)
			{
				nfloat x = _padding;

				if (_custTypeImageView != null)
				{
					var offset = (bounds.Height - size1.Height) / 2;
					_custTypeImageView.Frame = new CGRect(new CGPoint(x, offset), size1); ;
					x += size1.Width + _spacing;
				}

				if (_custTypeLabelView != null)
				{
					size2.Width = NMath.Min(size2.Width, bounds2.Width);
					var offset = (bounds.Height - size2.Height) / 2;
					_custTypeLabelView.Frame = new CGRect(new CGPoint(x, offset), size2); ;
				}

				if (_selectCust != null)
				{
					var offset = (bounds.Height - size3.Height) / 2;
					x = bounds.Width - size3.Width;
					_selectCust.Frame = new CGRect(new CGPoint(x, offset), size3);
				}
			}
			nfloat height = NMath.Max(size1.Height, NMath.Max(size2.Height, size3.Height));

			return new CGSize(bounds.Width, height + _padding + _padding);
		}

		public override CGSize SizeThatFits(CGSize size)
		{
			CGSize bounds = new CGSize(size.Width - _padding - _padding, size.Height);

			if (TitleMode)
				bounds = TitleModeSizing(bounds, false);
			else
				bounds = NormalModeSizing(bounds, false);

			bounds.Width += (_padding + _padding);
			return bounds;
		}

		public override void LayoutSubviews()
		{
			if (!BaseLayoutEngine.HasWindow(this))
				return;

			CGSize bounds = new CGSize(this.Bounds.Width - _padding - _padding, this.Bounds.Height);

			if (TitleMode)
				TitleModeSizing(bounds, true);
			else
				NormalModeSizing(bounds, true);

			/*
            UIGraphics.BeginImageContext(size3);
            using (CGContext context = UIGraphics.GetCurrentContext())
            {
                if (context != null)
                {
                    context.SaveState();
                    //300-27-5 = 268
                    _selectCust.Frame = new CGRect(268, offset3 + _padding, size3.Width, size3.Height) ;
                    _selectCust.Draw(_selectCust.Frame);
                    context.RestoreState();
                }
            }
            UIGraphics.EndImageContext();
            */
		}
	}

	class SelectAllButton : DetachableButton
	{
		internal SelectAllButton(StringHelper stringHelper, MobileCustomerSearchHelper helper) : base(new CGRect(0f, 0f, 80f, 30f))
		{
			Layer.BackgroundColor = StyleGuide.MCSC_SortOptTbl_LayerBgColor;
			//TouchDown += HandleTouchDown;
			string title;
			if (helper.AllSelected())
				title = stringHelper["wf_select_none"];
			else
				title = stringHelper["select_all"];
			this.SetTitle(title, UIControlState.Normal);
		}

		public SelectAllButton(IntPtr handle) : base(handle)
		{
			DebugLog.Trace("SelectAllButton(IntPtr) called!!!");
		}

		void HandleTouchDown(object sender, EventArgs e)
		{
		}

		void SetState()
		{
		}
	}
}
